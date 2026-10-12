using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace WorkChatMini;

public sealed partial class GeminiStoryProvider
{
    private const int MaxTransientAttempts = 2;
    private const string FastFallbackModel = "gemini-3.5-flash-lite";
    private const string SecondaryFallbackModel = "gemini-3.6-flash";
    private const int CapacityCooldownMilliseconds = 90000;
    private static readonly ConcurrentDictionary<string, long> CapacityCooldownUntil = new(StringComparer.OrdinalIgnoreCase);

    private static async Task ExecuteTextRequestWithResilienceAsync(
        string primaryModel,
        StoryAiRequest request,
        string apiKey,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        string activeModel = primaryModel;
        TimeSpan nonStreamingTimeout = TimeSpan.FromSeconds(Math.Clamp(request.NonStreamingTimeoutSeconds, 15, 120));
        bool skipPrimary = TryGetCapacityFallback(primaryModel, out string cooldownFallback);

        for (int modelPass = 0; modelPass < 2; modelPass++)
        {
            if (modelPass == 0)
            {
                activeModel = skipPrimary ? cooldownFallback : primaryModel;
                if (skipPrimary)
                {
                    AppLogger.Warn("STORY-AI",
                        $"Gemini capacity cooldown active; skipping primary model | primary={primaryModel} | fallback={activeModel}");
                }
            }
            else
            {
                if (skipPrimary || !ShouldTryFallback(lastError, primaryModel))
                    break;

                activeModel = SelectFallbackModel(primaryModel);
                AppLogger.Warn("STORY-AI",
                    $"Gemini primary model unavailable; trying fallback | primary={primaryModel} | fallback={activeModel}");
            }

            for (int attempt = 1; attempt <= MaxTransientAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool emittedText = false;
                Action<string> trackedDelta = delta =>
                {
                    if (!string.IsNullOrEmpty(delta))
                        emittedText = true;
                    onDelta(delta);
                };

                try
                {
                    string bodyJson = BuildBodyJson(request, activeModel);
                    bool useNonStreaming = request.PreferNonStreaming || Volatile.Read(ref _preferNonStreaming) != 0;
                    if (useNonStreaming)
                    {
                        await GenerateNonStreamingAsync(
                            activeModel, apiKey, bodyJson, nonStreamingTimeout, trackedDelta, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        try
                        {
                            await StreamSseAsync(
                                activeModel, apiKey, bodyJson, trackedDelta, cancellationToken).ConfigureAwait(false);
                        }
                        catch (GeminiHeadersTimeoutException ex) when (!cancellationToken.IsCancellationRequested)
                        {
                            Interlocked.Exchange(ref _preferNonStreaming, 1);
                            AppLogger.Warn("STORY-AI",
                                $"{ex.Message} Falling back to non-streaming Gemini requests for this session.");
                            await GenerateNonStreamingAsync(
                                activeModel, apiKey, bodyJson, nonStreamingTimeout, trackedDelta, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    ClearCapacityCooldown(activeModel);
                    if (!string.Equals(activeModel, primaryModel, StringComparison.OrdinalIgnoreCase))
                    {
                        AppLogger.Info("STORY-AI",
                            $"Gemini fallback recovered request | primary={primaryModel} | used={activeModel}");
                    }
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (CanRetryTransient(ex, emittedText))
                {
                    lastError = ex;
                    AppLogger.Warn("STORY-AI",
                        $"Gemini transient failure | model={activeModel} | attempt={attempt}/{MaxTransientAttempts} | {DescribeTransient(ex)}");

                    if (IsCapacityFailure(ex))
                    {
                        MarkCapacityCooldown(activeModel);
                        // A 429/503 is model capacity, not transport. Move to the fallback model
                        // immediately instead of wasting more time retrying the same overloaded model.
                        if (string.Equals(activeModel, primaryModel, StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                    // A 60-second timeout on the primary is just as disruptive as 503.
                    // Try a different model ONCE instead of waiting another minute.
                    if ((ex is TimeoutException or HttpRequestException) &&
                        string.Equals(activeModel, primaryModel, StringComparison.OrdinalIgnoreCase) &&
                        ShouldTryFallback(ex, primaryModel))
                    {
                        AppLogger.Warn("STORY-AI", $"Gemini primary timed out/network failed; trying fallback | primary={primaryModel}");
                        break;
                    }

                    if (attempt < MaxTransientAttempts)
                    {
                        TimeSpan delay = RetryDelay(attempt);
                        AppLogger.Info("STORY-AI",
                            $"Gemini retry scheduled | model={activeModel} | delayMs={(int)delay.TotalMilliseconds}");
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    break;
                }
            }
        }

        throw BuildResilienceFailure(primaryModel, activeModel, lastError);
    }

    private static async Task<StoryWebResearchResult> ExecuteResearchWithResilienceAsync(
        string primaryModel,
        string apiKey,
        string query,
        CancellationToken cancellationToken)
    {
        if (TryGetCapacityFallback(primaryModel, out _))
        {
            throw new InvalidOperationException(
                $"Gemini research skipped because model '{primaryModel}' is on a short capacity cooldown after a recent 429/503.");
        }

        Exception? lastError = null;
        for (int attempt = 1; attempt <= MaxTransientAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                StoryWebResearchResult result = await ResearchOnceAsync(
                    primaryModel, apiKey, query, cancellationToken).ConfigureAwait(false);
                ClearCapacityCooldown(primaryModel);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (CanRetryTransient(ex, emittedText: false))
            {
                lastError = ex;
                AppLogger.Warn("STORY-RESEARCH",
                    $"Gemini research transient failure | model={primaryModel} | attempt={attempt}/{MaxTransientAttempts} | {DescribeTransient(ex)}");
                if (IsCapacityFailure(ex))
                    MarkCapacityCooldown(primaryModel);
                if (attempt < MaxTransientAttempts)
                {
                    await Task.Delay(RetryDelay(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }
        }

        throw lastError ?? new InvalidOperationException("Gemini research request failed after automatic retries.");
    }

    private static bool CanRetryTransient(Exception ex, bool emittedText)
    {
        if (emittedText)
            return false;

        if (ex is GeminiApiException api)
            return IsTransientStatus(api.StatusCode);

        return ex is HttpRequestException or TimeoutException;
    }

    private static bool ShouldTryFallback(Exception? ex, string primaryModel)
    {
        bool overload = ex is GeminiApiException api && IsCapacityStatus(api.StatusCode);
        bool transportTimeout = ex is TimeoutException or HttpRequestException;
        if (!overload && !transportTimeout)
            return false;

        string fallback = SelectFallbackModel(primaryModel);
        return !string.Equals(primaryModel, fallback, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500;
    }

    private static bool IsCapacityFailure(Exception ex) =>
        ex is GeminiApiException api && IsCapacityStatus(api.StatusCode);

    private static bool IsCapacityStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static string SelectFallbackModel(string primaryModel) =>
        string.Equals(primaryModel, FastFallbackModel, StringComparison.OrdinalIgnoreCase)
            ? SecondaryFallbackModel
            : FastFallbackModel;

    private static bool TryGetCapacityFallback(string primaryModel, out string fallback)
    {
        fallback = SelectFallbackModel(primaryModel);
        if (string.Equals(primaryModel, fallback, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!CapacityCooldownUntil.TryGetValue(primaryModel, out long until))
            return false;

        long now = Environment.TickCount64;
        if (now < until)
            return true;

        CapacityCooldownUntil.TryRemove(primaryModel, out _);
        return false;
    }

    private static void MarkCapacityCooldown(string model)
    {
        CapacityCooldownUntil[model] = Environment.TickCount64 + CapacityCooldownMilliseconds;
        AppLogger.Warn("STORY-AI",
            $"Gemini model placed on temporary capacity cooldown | model={model} | seconds={CapacityCooldownMilliseconds / 1000}");
    }

    private static void ClearCapacityCooldown(string model) =>
        CapacityCooldownUntil.TryRemove(model, out _);

    private static TimeSpan RetryDelay(int failedAttempt)
    {
        int baseMs = failedAttempt <= 1 ? 700 : 1500;
        int jitterMs = Random.Shared.Next(80, 321);
        return TimeSpan.FromMilliseconds(baseMs + jitterMs);
    }

    private static string DescribeTransient(Exception ex) => ex switch
    {
        GeminiApiException api => $"status={(int)api.StatusCode} {api.StatusCode} | model={api.Model}",
        TimeoutException => "timeout",
        HttpRequestException => "network error",
        _ => ex.GetType().Name
    };

    private static Exception BuildResilienceFailure(string primaryModel, string lastModel, Exception? lastError)
    {
        if (lastError is GeminiApiException api && api.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            bool usedFallback = !string.Equals(primaryModel, lastModel, StringComparison.OrdinalIgnoreCase);
            string fallbackText = usedFallback ? $" and fallback '{lastModel}'" : string.Empty;
            return new InvalidOperationException(
                $"Gemini is temporarily overloaded (503). WorkChat tried '{primaryModel}'{fallbackText}, but the service is still unavailable. Please try again shortly.");
        }

        if (lastError is GeminiApiException rate && rate.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new InvalidOperationException(
                "Gemini rate limit is temporarily exhausted (429). WorkChat switched/retried automatically; please wait a moment and try again.");
        }

        return lastError ?? new InvalidOperationException("Gemini request failed after automatic retries.");
    }

    private static async Task<GeminiApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        string model,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string compactBody = Trim(body.Replace('\r', ' ').Replace('\n', ' '));
        AppLogger.Warn("STORY-AI",
            $"Gemini API error | model={model} | status={(int)response.StatusCode} | body={compactBody}");
        return new GeminiApiException(response.StatusCode, model, body);
    }

    private sealed class GeminiApiException : Exception
    {
        public GeminiApiException(HttpStatusCode statusCode, string model, string responseBody)
            : base(BuildMessage(statusCode, model))
        {
            StatusCode = statusCode;
            Model = model;
            ResponseBody = responseBody;
        }

        public HttpStatusCode StatusCode { get; }
        public string Model { get; }
        public string ResponseBody { get; }

        private static string BuildMessage(HttpStatusCode statusCode, string model) => statusCode switch
        {
            HttpStatusCode.ServiceUnavailable => $"Gemini model '{model}' is temporarily overloaded (503).",
            HttpStatusCode.TooManyRequests => $"Gemini model '{model}' is rate limited (429).",
            _ => $"Gemini API {(int)statusCode} {statusCode} for model '{model}'."
        };
    }
}
