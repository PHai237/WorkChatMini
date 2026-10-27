using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace WorkChatMini;

public partial class ReadingHubView : System.Windows.Controls.UserControl
{
    public ReadingHubView() => InitializeComponent();

    public StackPanel OpenTabs => ReadingOpenTabsInner;
    public StackPanel Shelf => ReadingShelfInner;
    public WpfTextBox Search => ReadingSearchInputInner;
    public TextBlock EditorHeading => ReadingEditorHeading;
    public WpfTextBox TitleInput => ReadingTitleInputInner;
    public WpfTextBox UrlInput => ReadingUrlInputInner;
    public TextBlock CountLabel => ReadingCountTextInner;
    public TextBlock MessageLabel => ReadingMessageTextInner;
    public System.Windows.Controls.Border AddForm => ReadingAddForm;
    public WpfButton ToggleAddButton => ToggleAddReadingButton;
    public WpfButton AddButton => AddReadingButton;
    public WpfButton CancelButton => CancelReadingButton;
    public WpfButton CloseButton => CloseReadingButton;
    public WpfButton CheckUpdatesButton => CheckReadingUpdatesButton;
    public WpfButton SaveCurrentButton => SaveCurrentReadingButton;
    public WpfButton MangaDexButton => MangaDexReadingButton;
    public WpfButton WebnovelButton => WebnovelReadingButton;
    public WpfButton HakoButton => HakoReadingButton;
    public System.Windows.Controls.Border ContinuePanel => ReadingContinuePanel;
    public TextBlock ContinueTitle => ReadingContinueTitle;
    public TextBlock ContinueMeta => ReadingContinueMeta;
    public System.Windows.Controls.ProgressBar ContinueProgress => ReadingContinueProgress;
    public WpfButton ContinueButton => ContinueReadingButton;
    public WpfButton FilterAll => ReadingFilterAll;
    public WpfButton FilterReading => ReadingFilterCurrent;
    public WpfButton FilterWaiting => ReadingFilterWaiting;
    public WpfButton FilterFavorites => ReadingFilterFavorites;
    public WpfButton ShowMoreButton => ReadingShowMoreButton;
}
