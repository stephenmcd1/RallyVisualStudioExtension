using RallyExtension.Extension.TeamExplorer;
using RallyExtension.Extension.ViewModels;

namespace RallyExtension.Extension
{
    /// <summary>
    /// Interaction logic for SectionContent.xaml
    /// </summary>
    public partial class SectionContent
    {
        public SectionContent(RallyWorkItemSection parentSection)
        {
            DataContext = new RallySectionContentViewModel(parentSection);

            InitializeComponent();
        }

        public RallySectionContentViewModel ViewModel => ((RallySectionContentViewModel) DataContext);
    }
}