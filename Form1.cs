using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MarkTrade;
using SharpCompress.Archive;
using SharpCompress.Reader;

namespace GetPDCLS
{
    public partial class Form1 : Form
    {
        private const string RAR_RESTR = "reestrx.rar";
        private Settings _settings = null;
        
        public Form1()
        {
            _settings = Settings.Read();

            InitializeComponent();

            if (_settings != null)
            {
                textBox1.Text = _settings.Path;
                numericUpDown1.Value = _settings.Interval;                
            }
            else
            {
                textBox1.Text = System.IO.Path.Combine(Application.StartupPath, RAR_RESTR);
            }

       
            string binDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(textBox1.Text),"bin");
            if (!Directory.Exists(binDir))
                Directory.CreateDirectory(binDir);

            string _rar = System.IO.Path.Combine(binDir, "Rar.exe");
            if (!System.IO.File.Exists(_rar))
                System.IO.File.WriteAllBytes(_rar, Resource1.Rar);

            string _template = System.IO.Path.Combine(binDir, "template.mde");
            if (!System.IO.File.Exists(_template))
            {
                //System.IO.File.WriteAllBytes(_template, Resource1.template);
                using (MemoryStream ms = new MemoryStream(Resource1.template))
                {
                    using (SharpCompress.Archive.SevenZip.SevenZipArchive zip= SharpCompress.Archive.SevenZip.SevenZipArchive.Open(ms))
                    {
                        using (IReader reader = zip.ExtractAllEntries())
                        {
                            if(reader.MoveToNextEntry())
                            if(reader.Entry.FilePath.ToLower().IndexOf("template.mde")>=0)
                                reader.WriteEntryTo(_template);
                        }                        
                    }                    
                }
                
            }

        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            _settings = _settings ?? new Settings();
            _settings.Interval = Convert.ToInt32( numericUpDown1.Value);
            _settings.Path = textBox1.Text;
            Settings.Write(_settings);
        }

        private void butLoop_Click(object sender, System.EventArgs e)
        {
            if (!timer1.Enabled)
            {
                butLoop.Text = "Выключить обновление";
                timer1.Interval = Convert.ToInt32(numericUpDown1.Value) * 60000;                
                timer1.Start();
                timer1_Tick(timer1, null);
            }
            else
            {
                butLoop.Text = "Включить обновление";
                timer1.Stop();
            }
        }

        private void timer1_Tick(object sender, System.EventArgs e)
        {
            System.Threading.Thread _thread = new Thread(new ParameterizedThreadStart((o) =>
            {
                try
                {
                    this.label3.BeginInvoke((Action)(() =>
                    {
                        label3.Text += "\r\nВыполняется обновление...";
                    }));

                    Converter _converter = new Converter(System.IO.Path.GetDirectoryName(o as string));
                    _converter.Convert();
                    this.label3.BeginInvoke((Action)(() =>
                    {
                        label3.Text = string.Format("Последний раз обновлялось: {0:dd.MM.yyyy HH:mm:ss}", DateTime.Now);
                    }));
                }
                catch (Exception)
                {
                }

            }));
            _thread.Start(textBox1.Text);
                      
        }

        private void butChoise_Click(object sender, EventArgs e)
        {
            if (this.folderBrowserDialog1.ShowDialog(this) == DialogResult.OK)
            {
                textBox1.Text = System.IO.Path.Combine(this.folderBrowserDialog1.SelectedPath,RAR_RESTR);
            }
        }

        private void butGetUrl_Click(object sender, EventArgs e)
        {
            UriBuilder builder = new UriBuilder();
            builder.Scheme = "file";
            builder.Path = textBox1.Text;
            Clipboard.SetText(builder.ToString());
            MessageBox.Show(this,
                "В буфер скопирована ссылка на файл с ценами!\r\nЗайдите в настройки программы, выбирите пункт \"Ссылка для скачивания реестра цен\" и вставте туда значение из буфера!", "Руководкство к дальнейшему действию", MessageBoxButtons.OK,MessageBoxIcon.Information);

        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            timer1.Interval = Convert.ToInt32(numericUpDown1.Value) * 60000;
        }

      
    }
}
