using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using YQLaser.UI.ViewModel;

namespace YQLaser.UI
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel viewModel;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.viewModel = new MainViewModel();
            this.viewModel.OnShowMsg = AppendText;
            this.DataContext = this.viewModel;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void AppendText(string txt)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (rtxtMsg.Document.Blocks.Count > 30)
                {
                    rtxtMsg.Document.Blocks.Clear();
                }
                rtxtMsg.AppendText(txt + Environment.NewLine);
            });
        }
    }
}
