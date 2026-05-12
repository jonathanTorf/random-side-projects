using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection.Metadata;
using System.Windows.Forms;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;

using DrawingColor = System.Drawing.Color;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using System.Runtime.CompilerServices;
using System.Windows.Forms.VisualStyles;

namespace almond
{
    public partial class Form1 : Form
    {
        float c = 0.025f;
        int renderMode = 2;
        float originalC;
        bool reletiveC = false;
        bool repeating = false;
        float flipStrangth = 0.1f;//0.4
        float wrapedStrangth = 1.5f; //0.75

        bool saveFrames = false;
        bool saveFinalImage = true;
        bool saveWebp = false;

        bool skipRenderWait = true;
        bool damping = false;
        bool showInfoText = false;

        bool explorer = true;
        bool doDeltaTime = false;
        int size;
        int maxSize;

        int loops = 1;
        float simulationTime = 5.5f;
        float timeStep = 0.005f;

        float l0 = 1f;
        float l0step = 0;
        float l1 = 1f;
        float l1step = 0;
        float m0 = 1f;
        float m0step = 0;
        float m1 = 1f;
        float m1step = 0;
        float g = 9.81f;

        int centerX = 0;
        int centerY = 0;

        int width;
        int height;

        String frameTime;
        float subStep;

        //int jDevider = 250;
        //int printDevider = -1;

        DrawingColor[] pixels;
        Bitmap framebuffer;
        Bitmap renderBuffer;
        List<Bitmap> gifFrames = new List<Bitmap>();
        Image<Rgba32> gif = null;
        dpData[] dpDataList;
        CancellationTokenSource simCancel;
        Task simTask;

        bool firstIt = true;

        float valMult = 1;

        struct dpData
        {
            public float dpTheta0;
            public float dpTheta1;
            public float dpV0;
            public float dpV1;
            public float dpOmega0;
            public float dpOmega1;
            public float dpWrapped;
            public int dpFlips;
            public bool dpFliped;
            public bool initialized;
        }

        public Form1()
        {
            Console.Write("Enter mode('t' for timelapse, 'e' for explorer): ");
            explorer = getConsoleChar('t', 'e', "Mode") == 'e';
            Console.Write("Render mode: ");
            renderMode = getConsoleInt(0, renderMode, "Render mode");
            reletiveC = !explorer;
            saveWebp = !explorer;
            
            if (explorer) Console.Write("Enter max size(to change this value you will have to restart the program): ");
            else Console.Write("Enter size: ");
            maxSize = getConsoleInt(50, 2500, "Size");

            if (!explorer)
            {
                Console.Write("Do delta time(each frame will continue from the previous instead of from 0) [y/n]: ");
                doDeltaTime = getConsoleChar('y', 'n', "Do delta time") == 'y';
                Console.Write("Enter time tick(how long each frame is simulated for): ");
                simulationTime = getConsoleFloat(0, 10, "Time tick");
                Console.Write("Enter total ticks(how many frames the program will simulate): ");
                loops = getConsoleInt(1, 1000000000, "Total ticks");

                Console.Write("Enter l0min(starting length of the first pendulums stick): ");
                l0 = getConsoleFloat(0, 1000, "l0min");
                if (l0 == 0) l0 = 1;
                else
                {
                    Console.Write("Enter l0step(how much length of the first pendulums stick increases by each frame): ");
                    l0step = getConsoleFloat(0, 1000, "l0step");
                }

                Console.Write("Enter l1min(starting length of the second pendulums stick): ");
                l1 = getConsoleFloat(0, 1000, "l1min");
                if (l1 == 0) l1 = 1;
                else
                {
                    Console.Write("Enter l1step(how much length of the second pendulums stick increases by each frame): ");
                    l1step = getConsoleFloat(0, 1000, "l1step");
                }

                Console.Write("Enter m0min(starting weight of the first pendulums ball): ");
                m0 = getConsoleFloat(0, 1000, "l0min");
                if (m0 == 0) m0 = 1;
                else
                {
                    Console.Write("Enter m0step(how much the weight of the first ball increases by each frame): ");
                    m0step = getConsoleFloat(0, 1000, "l0step");
                }

                Console.Write("Enter m1min(starting weight of the first pendulums ball): ");
                m1 = getConsoleFloat(0, 1000, "l0min");
                if (m1 == 0) m1 = 1;
                else
                {
                    Console.Write("Enter m1step(how much the weight of the first ball increases by each frame): ");
                    m1step = getConsoleFloat(0, 1000, "l0step");
                }
            }

            size = maxSize;
            width = size;
            height = size;
            originalC = c;

            pixels = new DrawingColor[width * height];
            framebuffer = new Bitmap(width, height);
            renderBuffer = new Bitmap(width, height);
            this.ClientSize = new DrawingSize(width, height);
            this.DoubleBuffered = true;
            this.KeyPreview = true;

            try
            {
                InitializeComponent();
                this.MouseClick += Form1_MouseClick;
                this.KeyDown += Form1_KeyDown;
                this.Load += Form1_Load;

            }
            catch (Exception ex)
            {
                Console.WriteLine("Program crashed:");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
        }

        async void cs()
        {
            simCancel?.Cancel();

            if (simTask != null)
                await simTask;

            StartSimulation();
        }

        int getConsoleInt(int min, int max, String name)
        {
            while (true)
            {
                string val = Console.ReadLine();
                if (int.TryParse(val, out var valI))
                {
                    valI = int.Parse(val);
                    if (valI < min || valI > max) Console.Write($"Invalid input entered, input must be between {min} and {max}: ");
                    else
                    {
                        Console.WriteLine($"{name} set to: {val}");
                        return valI;
                    }
                }
                else { Console.Write($"Invalid input entered, input must be an int: "); }
            }
        }

        float getConsoleFloat(float min, float max, String name)
        {
            while (true)
            {
                string val = Console.ReadLine();
                if (float.TryParse(val, out var valI))
                {
                    valI = float.Parse(val);
                    if (valI < min || valI > max) Console.Write($"Invalid input entered, input must be between {min} and {max}: ");
                    else
                    {
                        Console.WriteLine($"{name} set to: {val}");
                        return valI;
                    }
                }
                else { Console.Write($"Invalid input entered, input must be a float: "); }
            }
        }

        char getConsoleChar(char option0, char option1, String name)
        {
            while (true)
            {
                string val = Console.ReadLine();
                if (char.TryParse(val, out var valC))
                {
                    valC = char.Parse(val);
                    if (valC != option0 && valC != option1) Console.Write($"Invalid input entered, input must be ether '{option0}' or '{option1}': ");
                    else
                    {
                        Console.WriteLine($"{name} set to: {val}");
                        return valC;
                    }
                }
                else { Console.Write($"Invalid input entered, input must be a char: "); }
            }
        }

        private void Form1_MouseClick(object sender, MouseEventArgs e)
        {
            int px = e.X - size / 2 + centerX;
            int py = e.Y - size / 2 + centerY;

            if (e.Button == MouseButtons.Right && explorer)
            {
                centerX = px; centerY = py;
                if (!reletiveC) cs();

                Console.WriteLine($"Moving center to: {px}, {py}");
            }
            else { Console.WriteLine($"Clicked at: {px}, {py}"); }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.C)
            {
                Console.Clear();
                Console.WriteLine("Console cleared");
            }

            if (explorer)
            {
                if (e.KeyCode == Keys.W)
                {
                    centerY += 1 * (int)valMult;
                    Console.WriteLine($"CenterY increased to {centerY}");
                    if (!reletiveC) cs();

                }
                else if (e.KeyCode == Keys.A)
                {
                    centerX += 1 * (int)valMult;
                    Console.WriteLine($"CenterX increased to {centerX}");
                    if (!reletiveC) cs();

                }
                else if (e.KeyCode == Keys.S)
                {
                    centerY -= 1 * (int)valMult;
                    Console.WriteLine($"CenterY decreased to {centerY}");
                    if (!reletiveC) cs();

                }
                else if (e.KeyCode == Keys.D)
                {
                    centerX -= 1 * (int)valMult;
                    Console.WriteLine($"CenterX decreased to {centerX}");
                    if (!reletiveC) cs();

                }

                else if (e.KeyCode == Keys.Oemplus)
                {
                    c -= 0.0001f * valMult;
                    if (c <= 0) c = 0.0001f;
                    Console.WriteLine($"C decreased to {c}");
                    if (!reletiveC) cs();

                }
                else if (e.KeyCode == Keys.OemMinus)
                {
                    c += 0.0001f * valMult;
                    Console.WriteLine($"C increased to {c}");
                    if (!reletiveC) cs();

                }

                else if (e.KeyCode == Keys.O)
                {
                    l0 -= 0.1f * valMult;
                    if (l0 < 0.1f) l0 = 0.1f;
                    Console.WriteLine($"L0 decreased to {l0}");
                    cs();

                }
                else if (e.KeyCode == Keys.P)
                {
                    l0 += 0.1f * valMult;
                    Console.WriteLine($"L0 increased to {l0}");
                    cs();

                }

                else if (e.KeyCode == Keys.L)
                {
                    l1 -= 0.1f * valMult;
                    if (l1 < 0.1) l1 = 0.1f;
                    Console.WriteLine($"L1 decreased to {l1}");
                    cs();

                }
                else if (e.KeyCode == Keys.OemSemicolon)
                {
                    l1 += 0.1f * valMult;
                    Console.WriteLine($"L1 increased to {l1}");
                    cs();

                }

                else if (e.KeyCode == Keys.F1)
                {
                    saveToDownloads("almond");
                    Console.WriteLine("Saved as .png");
                }

                else if (e.KeyCode == Keys.ControlKey)
                {
                    valMult = 1;
                    Console.WriteLine($"valMult changed to {valMult}");
                }
                else if (e.KeyCode == Keys.ShiftKey)
                {
                    valMult = 10;
                    Console.WriteLine($"valMult changed to {valMult}");
                }
                else if (e.KeyCode == Keys.Z)
                {
                    valMult = 100;
                    Console.WriteLine($"valMult changed to {valMult}");
                }

                else if (e.KeyCode == Keys.R)
                {
                    reletiveC = !reletiveC;
                    Console.WriteLine($"reletiveC changed to {reletiveC}");
                    cs();
                }

                else if (e.KeyCode == Keys.Down)
                {
                    size -= 1 * (int)valMult;
                    if (size < 1) size = 1;
                    this.ClientSize = new DrawingSize(size, size);
                    Console.WriteLine($"Size decreased to {size}");
                    cs();

                }
                else if (e.KeyCode == Keys.Up)
                {
                    size += 1 * (int)valMult;
                    if (size > maxSize) size = maxSize;
                    this.ClientSize = new DrawingSize(size, size);
                    Console.WriteLine($"Size increased to {size}");
                    cs();
                }

                else if (e.KeyCode == Keys.NumPad0)
                {
                    Console.WriteLine("Render mode changed to 0");
                    renderMode = 0;
                    cs();
                }
                else if (e.KeyCode == Keys.NumPad1)
                {
                    Console.WriteLine("Render mode changed to 1");
                    renderMode = 1;
                    cs();
                }
                else if (e.KeyCode == Keys.NumPad2)
                {
                    Console.WriteLine("Render mode changed to 2");
                    renderMode = 2;
                    cs();
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            StartSimulation();
        }

        void StartSimulation()
        {
            simCancel = new CancellationTokenSource();

            simTask = Task.Run(() => runSimulation(simCancel.Token));
        }

        public void setPixel(int x, int y, DrawingColor c)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            pixels[x + y * width] = c;
        }

        void render()
        {
            var data = framebuffer.LockBits(
                new DrawingRectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            int[] argb = new int[width * height];
            for (int i = 0; i < argb.Length; i++)
            {
                argb[i] = pixels[i].ToArgb();
            }

            System.Runtime.InteropServices.Marshal.Copy(argb, 0, data.Scan0, argb.Length);

            framebuffer.UnlockBits(data);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode =
                System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

            lock (renderBuffer)
            {
                e.Graphics.DrawImage(renderBuffer,
                    new DrawingRectangle(0, 0, width, height));
            }
        }

        float toDegrees(float radians)
        {
            return MathF.Abs(radians * 180f / MathF.PI) % 360 - 180;
        }

        void calcPen(int ci, int cj)
        {
            float theta0, theta1, omega0, omega1, v0, v1, prevWrapped;
            int flips;
            bool flipped = false;
            int index = ci + cj * size;
            float Wrap(float a) => MathF.Atan2(MathF.Sin(a), MathF.Cos(a));

            if (!doDeltaTime || !dpDataList[index].initialized)
            {
                theta0 = (ci - size / 2) * c;
                theta1 = (cj - size / 2) * c;

                if (!reletiveC)
                {
                    theta0 += centerX * c;
                    theta1 += centerY * c;
                }

                if (MathF.Abs(theta0) > MathF.PI || MathF.Abs(theta1) > MathF.PI)
                {
                    setPixel(ci, cj, DrawingColor.Black);
                    return;
                }

                omega0 = 0;
                omega1 = 0;
                v0 = 0;
                v1 = 0;
                flips = 0;

                prevWrapped = Wrap(theta0);
            }
            else
            {
                var d = dpDataList[index];
                theta0 = d.dpTheta0;
                theta1 = d.dpTheta1;
                omega0 = d.dpOmega0;
                omega1 = d.dpOmega1;
                v0 = d.dpV0;
                v1 = d.dpV1;
                prevWrapped = d.dpWrapped;
                flips = d.dpFlips;
                flipped = d.dpFliped;
            }
            if (flipped && renderMode == 1) return;

            float localSubStep = subStep;

            for (double i = 0; i < simulationTime; i += timeStep)
            {
                for (int k = 0; k < 10; k++)
                {
                    float d = theta0 - theta1;
                    float sin_d = MathF.Sin(d);
                    float cos_d = MathF.Cos(d);

                    float bottom = l1 * (2 * m0 + m1 - m1 * MathF.Cos(2 * d));

                    float top0 =
                        -g * (2 * m0 + m1) * MathF.Sin(theta0)
                        - m1 * g * MathF.Sin(theta0 - 2 * theta1)
                        - 2 * sin_d * m1 *
                          (v1 * v1 * l1 + v0 * v0 * l0 * cos_d);

                    float top1 =
                        2 * sin_d *
                        (v0 * v0 * l0 * (m0 + m1)
                        + g * (m0 + m1) * MathF.Cos(theta0)
                        + v1 * v1 * l1 * m1 * cos_d);

                    omega0 += (top0 / bottom) * localSubStep;
                    omega1 += (top1 / bottom) * localSubStep;

                    theta0 += omega0 * localSubStep;
                    theta1 += omega1 * localSubStep;

                    float currWrapped = Wrap(theta0);
                    float delta = currWrapped - prevWrapped;

                    if (delta > MathF.PI)
                    {
                        flips--;
                        flipped = true;
                    }
                    if (delta < -MathF.PI)
                    {
                        flips++;
                        flipped = true;
                    }

                    prevWrapped = currWrapped;
                }
                if (damping)
                {
                    omega0 *= 0.999f;
                    omega1 *= 0.999f;
                }
            }

            //theta0list.Add(flips);
            float wrapped = MathF.Atan2(MathF.Sin(theta0), MathF.Cos(theta0));
            switch (renderMode)
            {
                case 0:
                    float hue = flips * flipStrangth + wrapped * wrapedStrangth + MathF.PI;
                    calcColor(hue, ci, cj);
                    break;
                case 1:
                    if (flipped) setPixel(ci, cj, DrawingColor.Red);
                    else setPixel(ci, cj, DrawingColor.White);
                    break;
                case 2:
                    if (theta0 > 0 && theta1 > 0) setPixel(ci, cj, DrawingColor.Red);
                    else if (theta0 < 0 && theta1 > 0) setPixel(ci, cj, DrawingColor.Blue);
                    else if (theta0 > 0 && theta1 < 0) setPixel(ci, cj, DrawingColor.Green);
                    else if (theta0 < 0 && theta1 < 0) setPixel(ci, cj, DrawingColor.Yellow);
                    else setPixel(ci, cj, DrawingColor.White);
                    break;
            }


            dpDataList[index] = new dpData
            {
                dpTheta0 = theta0,
                dpTheta1 = theta1,
                dpOmega0 = omega0,
                dpOmega1 = omega1,
                dpV0 = v0,
                dpV1 = v1,
                dpWrapped = wrapped,
                dpFlips = flips,
                dpFliped = flipped,
                initialized = true
            };
        }

        void calcColor(float ang, int x, int y)
        {
            float cr = (MathF.Sin(ang) + 1f) * 0.5f;
            float cg = (MathF.Sin(ang + 2f * MathF.PI / 3f) + 1f) * 0.5f;
            float cb = (MathF.Sin(ang + 4f * MathF.PI / 3f) + 1f) * 0.5f;
            setPixel(x, y, DrawingColor.FromArgb((int)(cr * 255), (int)(cg * 255), (int)(cb * 255)));
        }

        void saveToDownloads(string fileName)
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            String cdata = $"rC-{reletiveC}_c-{c}";

            if (!reletiveC) cdata += $"_cX-{centerX}_cY-{centerY}";
            String fn = $"{fileName}_dNt-{DateTime.Now:yyyyMMdd_HHmmss}_rt-{frameTime}_size-{size}_st-{simulationTime}s_{cdata}_l0-{l0}_l1-{l1}_m0-{m0}_m1-{m1}";
            string filePath = Path.Combine(downloads, fn);
            if (filePath.Length > 250) { filePath = filePath.Substring(0, 250); }
            filePath += ".png";

            framebuffer.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

            if (showInfoText) Console.WriteLine($"Image saved to: {filePath}");
        }

        void saveAsWebp()
        {
            Console.WriteLine($"Compiling webp(frame count: {gifFrames.Count})");

            foreach (var bmp in gifFrames)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var img = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                if (gif == null)
                    gif = img;
                else
                    gif.Frames.AddFrame(img.Frames.RootFrame);
            }

            var encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = 85
            };

            gif.Metadata.GetWebpMetadata().RepeatCount = 0;

            foreach (var frame in gif.Frames)
            {
                frame.Metadata.GetWebpMetadata().FrameDelay = 50; // ms
            }

            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"
            );

            string path = Path.Combine(
                downloads,
                $"almond_{DateTime.Now:yyyyMMdd_HHmmss}.webp"
            );

            gif.Save(path, encoder);

            Console.WriteLine($"WebP saved to: {path}");
        }

        String formatTime(long ms)
        {
            String convertedTime;
            if (ms < 1000)
            {
                convertedTime = $"{ms}ms";
            }
            else if (ms / 1000 < 60)
                convertedTime = $"{ms / 1000}s";
            else
            {
                float m = MathF.Floor((float)(ms / 1000 / 60));
                float s = MathF.Floor((float)(ms / 1000 % 60));
                convertedTime = $"{m};{s}m";
            }
            return convertedTime;
        }

        void runSimulation(CancellationToken token)
        {
            Array.Clear(pixels, 0, pixels.Length);
            dpDataList = new dpData[size * size];
            long prevTime = 0;

            var totalTime = Stopwatch.StartNew();
            for (int loop = 0; loop < loops; loop++)
            {
                if (token.IsCancellationRequested) return;
                this.Invoke(() => { this.ClientSize = new DrawingSize(size, size); });

                if (reletiveC) c = MathF.PI * 2 / size;
                subStep = timeStep / 10;

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount / 2
                };
                //int k = 0;
                int localSize = size;
                int logStep = (int)(localSize / 10);

                var sw = Stopwatch.StartNew();
                if (loop % (int)(loops / 10 + 1) == 0)
                {
                    long time = totalTime.ElapsedMilliseconds;
                    if (!explorer) Console.WriteLine($"Simulating frame: {loop} / {loops} at: T = {formatTime(time)} | DT = {formatTime(time - prevTime)}.");
                    prevTime = totalTime.ElapsedMilliseconds;
                }
                Parallel.For(0, localSize, options, i =>
                {
                    for (int j = 0; j < localSize; j++)
                    {
                        calcPen(i, j);
                    }
                    if (i % logStep == 0 && showInfoText) Console.WriteLine($"{i} of {size} rows rendering at: T = {sw.ElapsedMilliseconds}ms");
                });
                sw.Stop();

                l0 += l0step;
                l1 += l1step;
                m0 += m0step;
                m1 += m1step;

                frameTime = formatTime(sw.ElapsedMilliseconds);
                if (showInfoText) Console.WriteLine($"Simulation ended at: T = {frameTime}");

                if (showInfoText) Console.WriteLine("Rendering canvas...");
                this.BackColor = DrawingColor.Black;
                this.Invoke(() =>
                {
                    render();

                    lock (renderBuffer)
                    {
                        using (Graphics g = Graphics.FromImage(renderBuffer))
                        {
                            g.DrawImageUnscaled(framebuffer, 0, 0);
                        }
                    }

                    Invalidate();

                    if (saveWebp) gifFrames.Add((Bitmap)renderBuffer.Clone());
                });
                if (saveFrames) saveToDownloads("almond");
            }
            if (token.IsCancellationRequested) return;
            if (saveWebp) saveAsWebp();
            if (saveFinalImage) saveToDownloads("almondF");
            totalTime.Stop();
            Console.WriteLine($"\nTotal time: {formatTime(totalTime.ElapsedMilliseconds)}");
            if (!skipRenderWait)
            {
                for (int i = 0; i < 3; i++)
                {
                    Console.WriteLine($"Rendering in {i * -1 + 3}...");
                    Thread.Sleep(1000);
                }
            }
        }
    }
}