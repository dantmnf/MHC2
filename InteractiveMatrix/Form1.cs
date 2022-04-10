using Microsoft.Win32.TaskScheduler;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InteractiveMatrix
{
    public partial class Form1 : Form
    {
        private bool autoapply = false;
        private TrackBar[] bars;
        public Form1()
        {
            InitializeComponent();
            bars = new TrackBar[]
            {
                trackBar1,
                trackBar2,
                trackBar3,
                trackBar4,
                trackBar5,
                trackBar6,
                trackBar7,
                trackBar8,
                trackBar9,
                trackBar10,
                trackBar11,
                trackBar12,
            };
        }
        private const string iccpath = @"C:\Windows\System32\spool\drivers\color\nvIccAdvancedColorIdentity.icm";

        [DllImport("mscms")]
        private static extern void InternalRefreshCalibration(nint a, nint b, nint c, nint d);

        private void trackBar_ValueChanged(object sender, EventArgs e)
        {
            var tag = ((TrackBar)sender).Tag;
            if (tag == null) return;
            var idx = (int)tag;
            if (autoapply) ApplyValues();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            for (int i = 0; i < 12; i++)
            {
                bars[i].Tag = i;
            }
            RefreshValues();
        }

        private void RefreshValues()
        {
            using var fs = File.Open(iccpath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(0x0C78, SeekOrigin.Begin);
            using var reader = new BinaryReader(fs);
            for (int i = 0; i < 12; i++)
            {
                var u32be = reader.ReadUInt32();
                var u32le = BinaryPrimitives.ReverseEndianness(u32be);
                var s32le = unchecked((int)u32le);
                var value = s32le * 0.000015258789;
                var hval = (int)Math.Round(value * 100);
                bars[i].Value = hval;
            }
        }

        private void ApplyValues()
        {
            try
            {
                using var fs = File.Open(iccpath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                fs.Seek(0x0C78, SeekOrigin.Begin);
                using var writer = new BinaryWriter(fs);
                for (int i = 0; i < 12; i++)
                {
                    var hval = bars[i].Value;
                    var value = hval / 100.0;
                    var s32le = (int)Math.Round(value / 0.000015258789);
                    var s32be = BinaryPrimitives.ReverseEndianness(s32le);
                    writer.Write(s32be);
                }
                fs.Flush();
                
                //InternalRefreshCalibration(0, 0, 0, 0);
                ReloadCalibration();
            } catch (IOException) { /* ignore */ }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var oldapply = autoapply;
            autoapply = false;
            RefreshValues();
            autoapply = oldapply;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            autoapply = checkBox1.Checked;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            ApplyValues();
        }

        private static void ReloadCalibration()
        {
            using var ts = new TaskService();
            var task = ts.GetTask(@"\Microsoft\Windows\WindowsColorSystem\Calibration Loader");
            task.Run();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            var oldapply = autoapply;
            autoapply = false;
            trackBar1.Value = 100;
            trackBar2 .Value = 0;
            trackBar3 .Value = 0;
            trackBar4 .Value = 0;
            trackBar5 .Value = 0;
            trackBar6 .Value = 100;
            trackBar7 .Value = 0;
            trackBar8 .Value = 0;
            trackBar9 .Value = 0;
            trackBar10.Value = 0;
            trackBar11.Value = 100;
            trackBar12.Value = 0;
            autoapply = oldapply;
            ApplyValues();
        }
    }
}