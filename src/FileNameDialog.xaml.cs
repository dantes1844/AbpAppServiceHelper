using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MadsKristensen.AddAnyFile;

namespace AbpAppServiceHelper
{
    public partial class FileNameDialog : Window
    {
        private const string DEFAULT_TEXT = "输入实体类名称，单数形式";
        private static List<string> _tips = new List<string> {
            "自动生成复数的实体文件夹，内含Dto文件夹及几个基础的Dto类。另外还包含接口和业务类"
        };

        public FileNameDialog(string folder)
        {
            InitializeComponent();

            lblFolder.Content = $"{folder}/";

            Loaded += (s, e) =>
            {
                Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AbpAppServiceHelper;component/Resources/icon.png", UriKind.RelativeOrAbsolute));
                Title = Vsix.Name;
                SetRandomTip();

                txtName.Focus();
                txtName.CaretIndex = 0;
                txtName.Text = DEFAULT_TEXT;
                txtName.Select(0, txtName.Text.Length);

                txtName.PreviewKeyDown += (a, b) =>
                {
                    if (b.Key == Key.Escape)
                    {
                        if (string.IsNullOrWhiteSpace(txtName.Text) || txtName.Text == DEFAULT_TEXT)
                            Close();
                        else
                            txtName.Text = string.Empty;
                    }
                    else if (txtName.Text == DEFAULT_TEXT)
                    {
                        txtName.Text = string.Empty;
                        btnCreate.IsEnabled = true;
                    }
                };

            };
        }

        public string Input
        {
            get { return txtName.Text.Trim(); }
        }

        private void SetRandomTip()
        {
            var rnd = new Random(DateTime.Now.GetHashCode());
            int index = rnd.Next(_tips.Count);
            lblTips.Content = _tips[index];
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
