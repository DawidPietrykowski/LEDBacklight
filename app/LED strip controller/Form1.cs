using System;
using System.Drawing;
using System.Windows.Forms;
using NAudio.Wave; // installed with nuget
using System.Numerics;

namespace LED_strip_controller
{
    public partial class led : Form
    {
        public BufferedWaveProvider bwp;
        public WaveIn wi;
        int BUFFERSIZE = (int)Math.Pow(2, 11);
        int RATE = 44100;
        //int maxval = 0;
        int[] FreqMag = new int[10];
        int[] ForColor = new int[10];
        float[,] FreqMagSmooth = new float[10, 2];
        float[] FMSA = new float[10];
        string calcMode = "max";
        int FALLSPEED = 50;
        int FALLDELAY = 5;
        Color[] saved = new Color[3];
        Color averaged = Color.FromArgb(0, 0, 0);
        float[] sensitivity = { 30, 0.5f, 1f, 1f, 1f, 1f, 0.5f, 0.6f, 0.2f, 0.3f };
        int sum = 0;
        string[] cl = new string[5];
        float brightness = 50;
        Color finalColor;
        int animation = 0;
        bool audioStream = false;
        bool uH = true;
        readonly Color[] colors = { Color.FromArgb(0,0,255),
        Color.FromArgb(255, 0, 50),
        Color.FromArgb(255, 0, 0),
        Color.FromArgb(255, 0, 0),
        Color.FromArgb(255, 0, 0),
        Color.FromArgb(255, 106, 0),
        Color.FromArgb(200, 100, 0),
        Color.FromArgb(100, 255, 0),
        Color.FromArgb(100, 255, 0),
        Color.FromArgb(100, 255, 0)};
        readonly Color[] colorsM = { Color.FromArgb(0,0,255),
        Color.FromArgb(150, 50, 255),
        Color.FromArgb(255, 0, 15),
        Color.FromArgb(255, 0, 0),
        Color.FromArgb(255, 0, 15),
        Color.FromArgb(255, 10, 0),
        Color.FromArgb(255, 10, 0),
        Color.FromArgb(255, 10, 0),
        Color.FromArgb(255, 10, 0),
        Color.FromArgb(255, 10, 0)};

        bool T2 = false;

        public led(string com_port, int device_id)
        {
            InitializeComponent();
            serialPort1.PortName = com_port;
            string[] device_names = new string[WaveIn.DeviceCount];
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            for (int n = 0; n < WaveIn.DeviceCount; n++)
            {
                var caps = WaveIn.GetCapabilities(n);
                device_names[n] = caps.ProductName;
                listBox1.Items.Add(device_names[n]);
            }
            listBox1.SelectionMode = SelectionMode.MultiExtended;

            wi = new WaveIn();
            wi.DeviceNumber = device_id;
            wi.WaveFormat = new NAudio.Wave.WaveFormat(RATE, 1);

            wi.BufferMilliseconds = (int)((double)BUFFERSIZE / (double)RATE * 1000);

            // create a wave buffer and start the recording
            wi.DataAvailable += new EventHandler<WaveInEventArgs>(wi_DataAvailable);
            bwp = new BufferedWaveProvider(wi.WaveFormat);
            bwp.BufferLength = BUFFERSIZE * 2;

            bwp.DiscardOnBufferOverflow = true;
            wi.StartRecording();

            timer1.Enabled = true;

            serialPort1.Open();

            //checkBox1.Enabled = false;
        }

        public double[] FFT(double[] data)
        {
            double[] fft = new double[data.Length]; // this is where we will store the output (fft)
            Complex[] fftComplex = new Complex[data.Length]; // the FFT function requires complex format
            for (int i = 0; i < data.Length; i++)
            {
                fftComplex[i] = new Complex(data[i], 0.0); // make it complex format (imaginary = 0)
            }
            Accord.Math.FourierTransform.FFT(fftComplex, Accord.Math.FourierTransform.Direction.Forward);
            for (int i = 0; i < data.Length; i++)
            {
                fft[i] = fftComplex[i].Magnitude; // back to double
                                                  //fft[i] = Math.Log10(fft[i]); // convert to dB
            }
            return fft;
        }
        public void UpdateAudioGraphTT()
        {

            // read the bytes from the stream
            int frameSize = BUFFERSIZE;
            var frames = new byte[frameSize];
            bwp.Read(frames, 0, frameSize);
            if (frames.Length == 0) return;
            if (frames[frameSize - 2] == 0) return;

            timer1.Enabled = false;

            // convert it to int32 manually (and a double for scottplot)
            int SAMPLE_RESOLUTION = 16;
            int BYTES_PER_POINT = SAMPLE_RESOLUTION / 8;
            Int32[] vals = new Int32[frames.Length / BYTES_PER_POINT];
            double[] Ys = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs = new double[frames.Length / BYTES_PER_POINT];
            double[] Ys2 = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs2 = new double[frames.Length / BYTES_PER_POINT];
            for (int i = 0; i < vals.Length; i++)
            {
                // bit shift the byte buffer into the right variable format
                byte hByte = frames[i * 2 + 1];
                byte lByte = frames[i * 2 + 0];
                vals[i] = (int)(short)((hByte << 8) | lByte);
                Xs[i] = i;
                Ys[i] = vals[i];
                Xs2[i] = (double)i / Ys.Length * RATE / 1000.0; // units are in kHz
            }

            Ys2 = FFT(Ys);

            //getting values for frequencies
            void getvalforfreq()
            {
                int val = 0;
                for (int i = 1; i <= 1; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                //val = (int)Math.Pow(Ys2[i]/1000,2)*1000;
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            float v = (float)(Math.Pow(Ys2[1] - 700, 3) * 0.00003) + 100;
                            float c = (float)(Ys2[1] * 0.1);
                            if (v > c)
                                val = (int)v;
                            else
                                val = (int)c;

                            //val = (int)Ys[1];
                            break;
                    }
                }
                FreqMag[0] = val;
                val = 0;

                for (int i = 2; i <= 2; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[1] = val;
                val = 0;

                for (int i = 3; i <= 4; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[2] = val;
                val = 0;

                for (int i = 5; i <= 8; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[3] = val;
                val = 0;

                for (int i = 9; i <= 15; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[4] = val;
                val = 0;

                for (int i = 16; i <= 30; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[5] = val;
                val = 0;

                for (int i = 31; i <= 60; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[6] = val;
                val = 0;

                for (int i = 61; i <= 120; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[7] = val;
                val = 0;

                for (int i = 121; i <= 240; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[8] = val;
                val = 0;

                for (int i = 241; i <= 400; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[9] = val;
                val = 0;
            }
            getvalforfreq();

            for (int i = 0; i < FreqMagSmooth.Length / 2; i++)
            {
                if (FreqMagSmooth[i, 0] > 1000) FreqMagSmooth[i, 0] = 1000;
            }

            for (int i = 0; i < FreqMagSmooth.Length / 2; i++)
            {
                FMSA[i] = FreqMagSmooth[i, 0];// * (10000) / sum);
            }

            float valr = 0;
            float valg = 0;
            float valb = 0;

            for (int i = 0; i < 10; i++)
            {
                valr += colors[i].R * (FMSA[i] / 1000);// * sensitivity[i];
                valg += colors[i].G * (FMSA[i] / 1000);// * sensitivity[i];
                valb += colors[i].B * (FMSA[i] / 1000);// * sensitivity[i];
            }

            //valr /= 10; valg /= 10; valb /= 10;
            if (valr > 255) valr = 255;
            if (valg > 255) valg = 255;
            if (valb > 255) valb = 255;

            finalColor = Color.FromArgb((int)valr, (int)valg, (int)valb);

            saved[0] = saved[1];
            saved[1] = finalColor;

            averaged = Color.FromArgb((saved[0].R + saved[1].R) / 2, (saved[0].G + saved[1].G) / 2, (saved[0].B + saved[1].B) / 2);

            //if (checkBox1.Checked)
            this.BackColor = averaged;


            timer1.Enabled = true;
        }

        public void UpdateAudioGraph()
        {

            // read the bytes from the stream
            int frameSize = BUFFERSIZE;
            var frames = new byte[frameSize];
            bwp.Read(frames, 0, frameSize);
            if (frames.Length == 0) return;
            if (frames[frameSize - 2] == 0) return;

            timer1.Enabled = false;

            // convert it to int32 manually (and a double for scottplot)
            int SAMPLE_RESOLUTION = 16;
            int BYTES_PER_POINT = SAMPLE_RESOLUTION / 8;
            Int32[] vals = new Int32[frames.Length / BYTES_PER_POINT];
            double[] Ys = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs = new double[frames.Length / BYTES_PER_POINT];
            double[] Ys2 = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs2 = new double[frames.Length / BYTES_PER_POINT];
            for (int i = 0; i < vals.Length; i++)
            {
                // bit shift the byte buffer into the right variable format
                byte hByte = frames[i * 2 + 1];
                byte lByte = frames[i * 2 + 0];
                vals[i] = (int)(short)((hByte << 8) | lByte);
                Xs[i] = i;
                Ys[i] = vals[i];
                Xs2[i] = (double)i / Ys.Length * RATE / 1000.0; // units are in kHz
            }

            Ys2 = FFT(Ys);

            //getting values for frequencies
            void getvalforfreq()
            {
                int val = 0;
                for (int i = 1; i <= 1; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                //val = (int)Math.Pow(Ys2[i]/1000,2)*1000;
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            float v = (float)(Math.Pow(Ys2[1] - 700, 3) * 0.00003) + 100;
                            float c = (float)(Ys2[1] * 0.1);
                            if (v > c)
                                val = (int)v;
                            else
                                val = (int)c;

                            //val = (int)Ys[1];
                            break;
                    }
                }
                FreqMag[0] = val;
                val = 0;

                for (int i = 2; i <= 2; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[1] = val;
                val = 0;

                for (int i = 3; i <= 4; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[2] = val;
                val = 0;

                for (int i = 5; i <= 8; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[3] = val;
                val = 0;

                for (int i = 9; i <= 15; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[4] = val;
                val = 0;

                for (int i = 16; i <= 30; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[5] = val;
                val = 0;

                for (int i = 31; i <= 60; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[6] = val;
                val = 0;

                for (int i = 61; i <= 120; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[7] = val;
                val = 0;

                for (int i = 121; i <= 240; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[8] = val;
                val = 0;

                for (int i = 241; i <= 400; i++)
                {
                    switch (calcMode)
                    {
                        case "max":
                            if (Ys2[i] > val)
                            {
                                val = (int)Ys2[i];
                            }
                            break;
                        case "sum":
                            val += (int)Ys2[i];
                            break;
                    }
                }
                FreqMag[9] = val;
                val = 0;
            }
            getvalforfreq();

            //update max value
            //if (Ys[0]>maxval) maxval = (int)Ys[0];

            //smoothing update
            if (FreqMag[0] > FreqMagSmooth[0, 0])
            {
                FreqMagSmooth[0, 0] = FreqMag[0];
                FreqMagSmooth[0, 1] = 0;
            }
            else if (FreqMagSmooth[0, 1] > 1)
            {
                if (FreqMagSmooth[0, 0] < 100)
                    FreqMagSmooth[0, 0] = 0;
                else if (FreqMagSmooth[0, 0] > 750)
                    FreqMagSmooth[0, 0] -= 100;//* FreqMagSmooth[i, 0] / 1000;
                else FreqMagSmooth[0, 0] = 0;
            }
            else
                FreqMagSmooth[0, 1] += 1;
            for (int i = 1; i < FreqMag.Length; i++)
            {
                if (FreqMag[i] > FreqMagSmooth[i, 0])
                {
                    FreqMagSmooth[i, 0] = FreqMag[i];
                    FreqMagSmooth[i, 1] = 0;
                }
                else if (FreqMagSmooth[i, 1] > FALLDELAY)
                {
                    if (FreqMagSmooth[i, 0] < 100)
                        FreqMagSmooth[i, 0] = 0;
                    else if (FreqMagSmooth[i, 0] > FALLSPEED)
                        FreqMagSmooth[i, 0] -= FALLSPEED;//* FreqMagSmooth[i, 0] / 1000;
                    else FreqMagSmooth[i, 0] = 0;
                }
                else
                    FreqMagSmooth[i, 1] += 1;
            }

            //upbound
            for (int i = 0; i < FreqMagSmooth.Length / 2; i++)
            {
                if (FreqMagSmooth[i, 0] > 1000) FreqMagSmooth[i, 0] = 1000;
            }

            //calculating volume
            sum = 0;
            for (int i = 0; i < FreqMagSmooth.Length / 2; i++) sum += (int)FreqMagSmooth[i, 0];
            //if (sum > 1000) sum = 1000;
            if (sum == 0) sum = 1;

            //adjusting for volume
            for (int i = 0; i < FreqMagSmooth.Length / 2; i++)
            {
                FMSA[i] = FreqMagSmooth[i, 0];// * (10000) / sum);
            }

            //clearing noise
            for (int i = 0; i < FMSA.Length / 2; i++)
            {
                if (FMSA[i] < 100) FMSA[i] = 0;
                if (FMSA[i] > 1000) FMSA[i] = 1000;
            }

            //calculating colors
            for (int i = 0; i < FreqMag.Length; i++)
            {
                if (FMSA[i] > 1000) FMSA[i] = 1000;

                if (FMSA[i] < 10) FMSA[i] = 0;
                //ForColor[i] = FreqMagSmooth[i, 0] * (1000 / sum);
                ForColor[i] = (int)(FMSA[i] * 1.25);

                if (ForColor[i] > 1000) ForColor[i] = 1000;
            }

            //float ab = 1;

            float valr = 0;
            float valg = 0;
            float valb = 0;
            if (!uH)
            {
                for (int i = 0; i < 10; i++)
                {
                    valr += colors[i].R * (FMSA[i] / 1000) * sensitivity[i];
                    valg += colors[i].G * (FMSA[i] / 1000) * sensitivity[i];
                    valb += colors[i].B * (FMSA[i] / 1000) * sensitivity[i];
                }
            }
            else
            {
                int h = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (FMSA[i] > FMSA[h]) h = i;
                }
                valr = colorsM[h].R * (FMSA[h] / 1000); //* 2.5f;
                valg = colorsM[h].G * (FMSA[h] / 1000); //* 2.5f;
                valb = colorsM[h].B * (FMSA[h] / 1000); //* 2.5f;
            }
            //valr /= 10; valg /= 10; valb /= 10;
            if (valr > 255) valr = 255;
            if (valg > 255) valg = 255;
            if (valb > 255) valb = 255;
            //brghtnss = Color.FromArgb((int)valr, (int)valg, (int)valb).GetBrightness();
            //if (brghtnss > 0.001f) ab = 1/brghtnss;
            //valr *= (ab * (brightness/100)); valg *= (ab * (brightness / 100)); valb *= (ab * (brightness / 100));
            if (valr > 255) valr = 255;
            if (valg > 255) valg = 255;
            if (valb > 255) valb = 255;

            finalColor = Color.FromArgb((int)valr, (int)valg, (int)valb);

            saved[0] = saved[1];
            saved[1] = finalColor;

            averaged = Color.FromArgb((saved[0].R + saved[1].R) / 2, (saved[0].G + saved[1].G) / 2, (saved[0].B + saved[1].B) / 2);

            //if (checkBox1.Checked)
            //this.BackColor = averaged;
            //else
            //this.BackColor = finalColor;

            //save[0] = Color.FromArgb((int)(ForColor[0] / 4f), (int)(ForColor[2] / 4), (int)(ForColor[1] / 4));
            //this.BackColor = Color.FromArgb((save[0].R + save[1].R + save[2].R ) / 3, (save[0].G + save[1].G + save[2].R) / 3, (save[0].B + save[1].B + save[2].R) / 3);
            //this.BackColor = Color.FromArgb(save[2].R, save[2].G, save[2].B);
            //this.BackColor = Color.FromArgb((int)(ForColor[0] / 4f), (int)(ForColor[2] / 4), (int)(ForColor[1] / 4));

            //updating bars and labels
            /*
            progressBar1.Value = (int)FreqMagSmooth[0, 0];
            progressBar2.Value = (int)FreqMagSmooth[1, 0];
            progressBar3.Value = (int)FreqMagSmooth[2, 0];
            progressBar4.Value = (int)FreqMagSmooth[3, 0];
            progressBar5.Value = (int)FreqMagSmooth[4, 0];
            progressBar6.Value = (int)FreqMagSmooth[5, 0];
            progressBar7.Value = (int)FreqMagSmooth[6, 0];
            progressBar8.Value = (int)FreqMagSmooth[7, 0];
            progressBar9.Value = (int)FreqMagSmooth[8, 0];
            progressBar10.Value = (int)FreqMagSmooth[9, 0];
            progressBar11.Value = sum / 10;
            label5.Text = maxval.ToString();
            label7.Text = averaged.R.ToString();
            label6.Text = ((int)(Xs2[Convert.ToInt32(textBox1.Text)] * 1000)).ToString();
            label9.Text = timer2.Interval.ToString();
            label10.Text = FALLDELAY.ToString();
            label11.Text = FALLSPEED.ToString();
            */

            timer1.Enabled = true;
        }
        private void update()
        {
            brightness = trackBar1.Value;
            label1.Text = trackBar1.Value.ToString();
            if ((animation == 2) || (animation == 5) || (animation == 6)) audioStream = true;
            else audioStream = false;

            if (audioStream)
            {
                sendDataToSerial(averaged.R, averaged.G, averaged.B, animation);
            }

           // if (!audioStream) this.BackColor = Color.FromArgb(42, 42, 42);
        }

        public void sendDataToSerial(int r, int g, int b, int a)
        {
            label2.Text = r.ToString();
            label3.Text = g.ToString();
            label4.Text = b.ToString();

            byte[] buffer = new byte[5];

            buffer[0] = (byte)r;
            buffer[1] = (byte)g;
            buffer[2] = (byte)b;
            buffer[3] = (byte)a;
            buffer[4] = (byte)brightness;

            serialPort1.Write(buffer, 0, 5);
            serialPort1.DiscardOutBuffer();
            serialPort1.DiscardInBuffer();
        }

        public void sendModeToSerial()
        {
            if (textBox1.Text != "")
                animation = int.Parse(textBox1.Text);
            else
                animation = 0;

            byte[] buffer = new byte[5];

            buffer[0] = 0;
            buffer[1] = 0;
            buffer[2] = 0;
            buffer[3] = (byte)animation;
            buffer[4] = (byte)brightness;

            serialPort1.Write(buffer, 0, 5);
            serialPort1.DiscardOutBuffer();
            serialPort1.DiscardInBuffer();
        }

        void wi_DataAvailable(object sender, WaveInEventArgs e)
        {
            bwp.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void TextBox1_TextChanged_1(object sender, EventArgs e)
        {
            if (textBox1.Text != "")
                animation = int.Parse(textBox1.Text);
            //sendModeToSerial();

            //if (animation == 1)
            //{
                sendDataToSerial(trackBar2.Value, trackBar3.Value, trackBar4.Value, animation);
            //}
        }

        private void TrackBar2_Scroll(object sender, EventArgs e)
        {
            if (!audioStream)
                sendDataToSerial(trackBar2.Value, trackBar3.Value, trackBar4.Value, animation);
        }

        private void TrackBar3_Scroll_1(object sender, EventArgs e)
        {
            if (!audioStream)
                sendDataToSerial(trackBar2.Value, trackBar3.Value, trackBar4.Value, animation);
        }

        private void TrackBar4_Scroll(object sender, EventArgs e)
        {
            if (!audioStream)
                sendDataToSerial(trackBar2.Value, trackBar3.Value, trackBar4.Value, animation);
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            if (audioStream)
            {
                if (!T2)
                    UpdateAudioGraph();
                else
                    UpdateAudioGraphTT();
            }
            update();
        }

        private void TrackBar1_Scroll(object sender, EventArgs e)
        {
            if (!audioStream)
                sendDataToSerial(trackBar2.Value, trackBar3.Value, trackBar4.Value, animation);
            else
                sendDataToSerial(averaged.R, averaged.G, averaged.B, animation);

        }

        private void Label4_Click(object sender, EventArgs e)
        {

        }

        private void Label3_Click(object sender, EventArgs e)
        {

        }

        private void Label2_Click(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void CheckBox1_CheckedChanged(object sender, EventArgs e)
        {
            T2 = checkBox1.Checked;
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            wi.DeviceNumber = listBox1.SelectedIndex;
        }

    }
}
