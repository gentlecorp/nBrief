
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace nBriefcase
{
    public class CreateBriefcaseForm : Form
    {
        private TextBox nameBox;
        private TextBox pathBox;
        private ComboBox sizeBox;
        private Button browseButton;
        private Button createButton;
        private Button cancelButton;

        public string BriefcaseName
        {
            get { return nameBox.Text.Trim(); }
        }

        public string DestinationPath
        {
            get { return pathBox.Text.Trim(); }
        }

        public int CapacityGB
        {
            get { return Convert.ToInt32(sizeBox.SelectedItem); }
        }

        public CreateBriefcaseForm()
        {
            Text = "새 서류가방 만들기";
            Size = new Size(470, 285);
            MinimumSize = new Size(470, 285);
            MaximumSize = new Size(470, 285);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label nameLabel = new Label();
            nameLabel.Text = "서류가방 이름";
            nameLabel.Location = new Point(20, 20);
            nameLabel.AutoSize = true;
            Controls.Add(nameLabel);

            nameBox = new TextBox();
            nameBox.Location = new Point(140, 17);
            nameBox.Size = new Size(285, 25);
            nameBox.Text = "MyBriefcase";
            Controls.Add(nameBox);

            Label pathLabel = new Label();
            pathLabel.Text = "저장 위치";
            pathLabel.Location = new Point(20, 60);
            pathLabel.AutoSize = true;
            Controls.Add(pathLabel);

            pathBox = new TextBox();
            pathBox.Location = new Point(140, 57);
            pathBox.Size = new Size(205, 25);
            pathBox.Text = "";
            Controls.Add(pathBox);

            browseButton = new Button();
            browseButton.Text = "찾아보기...";
            browseButton.Location = new Point(350, 55);
            browseButton.Size = new Size(75, 28);
            browseButton.Click += BrowseButton_Click;
            Controls.Add(browseButton);

            Label sizeLabel = new Label();
            sizeLabel.Text = "최대 용량";
            sizeLabel.Location = new Point(20, 100);
            sizeLabel.AutoSize = true;
            Controls.Add(sizeLabel);

            sizeBox = new ComboBox();
            sizeBox.Location = new Point(140, 97);
            sizeBox.Size = new Size(100, 25);
            sizeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sizeBox.Items.AddRange(new object[]
            {
                32, 64, 128, 256, 512
            });
            sizeBox.SelectedItem = 128;
            Controls.Add(sizeBox);

            Label gbLabel = new Label();
            gbLabel.Text = "GB (동적 할당)";
            gbLabel.Location = new Point(250, 101);
            gbLabel.AutoSize = true;
            Controls.Add(gbLabel);

            Label infoLabel = new Label();
            infoLabel.Text =
                "VHDX는 실제 사용량에 따라 커집니다.\r\n" +
                "USB의 파일시스템과 남은 공간을 확인하세요.";
            infoLabel.Location = new Point(20, 140);
            infoLabel.Size = new Size(405, 42);
            Controls.Add(infoLabel);

            createButton = new Button();
            createButton.Text = "생성";
            createButton.Location = new Point(260, 195);
            createButton.Size = new Size(80, 30);
            createButton.DialogResult = DialogResult.OK;
            Controls.Add(createButton);

            cancelButton = new Button();
            cancelButton.Text = "취소";
            cancelButton.Location = new Point(345, 195);
            cancelButton.Size = new Size(80, 30);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);

            AcceptButton = createButton;
            CancelButton = cancelButton;
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog =
                new FolderBrowserDialog())
            {
                dialog.Description = "서류가방을 저장할 폴더를 선택하세요.";

                if (Directory.Exists(pathBox.Text))
                    dialog.SelectedPath = pathBox.Text;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    pathBox.Text = dialog.SelectedPath;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(BriefcaseName))
                {
                    MessageBox.Show(
                        this, "서류가방 이름을 입력하세요.",
                        "입력 확인",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    e.Cancel = true;
                }
                else if (BriefcaseName.IndexOfAny(
                    Path.GetInvalidFileNameChars()) >= 0 ||
                    BriefcaseName == "." ||
                    BriefcaseName == "..")
                {
                    MessageBox.Show(
                        this, "파일 이름에 사용할 수 없는 문자가 있습니다.",
                        "입력 확인",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    e.Cancel = true;
                }
                else if (!Directory.Exists(DestinationPath))
                {
                    MessageBox.Show(
                        this, "저장 위치를 선택하세요.",
                        "입력 확인",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    e.Cancel = true;
                }
            }

            base.OnFormClosing(e);
        }
    }
}
