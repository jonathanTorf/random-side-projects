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

namespace almond
{
    public partial class Form1 : Form
    {
        float c = 0.005f;
        bool reletiveC = true;
        float flipStrangth = 0.02f;//0.4
        float wrapedStrangth = 1f; //0.75

        bool saveFrames = false;
        bool saveFinalImage = true;
        bool saveWebp = true;

        bool skipRenderWait = false;
        bool damping = false;
        bool showInfoText = false;

        bool doDeltaTime = true;
        int size = 500;

        int loops = 500;
        float simulationTime = 0.1f;
        float timeStep = 0.01f;

        float l0 = 1f;
        float l1 = 1f;
        float m0 = 1f;
        float m1 = 1f;
        float g = 9.81f;

        float centerX = 1.2f;
        float centerY = 0.5f;

        int width = 1;
        int height = 1;

        String frameTime;
        float subStep;

        //int jDevider = 250;
        //int printDevider = -1;

        DrawingColor[] pixels;
        Bitmap framebuffer;
        List<Bitmap> gifFrames = new List<Bitmap>();
        Image<Rgba32> gif = null;
        dpData[] dpDataList;

        bool firstIt = true;

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
            public bool initialized;
        }

        public Form1()
        {
            try
            {
                InitializeComponent();
                dpDataList = new dpData[size * size];
                long prevTime = 0;

                var totalTime = Stopwatch.StartNew();
                for (int loop = 0; loop < loops; loop++)
                {
                    //l1 = loop + 1;
                    //simulationTime = loop * 0.1f;
                    width = size;
                    height = size;

                    pixels = new DrawingColor[width * height];
                    framebuffer = new Bitmap(width, height);

                    this.ClientSize = new DrawingSize(width, height);

                    if (reletiveC) c = MathF.PI * 2 / size;
                    subStep = timeStep / 10;

                    //setPixel(10, 10, DrawingColor.Red);
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
                        Console.WriteLine($"Simulating frame: {loop} / {loops} at: T = {formatTime(time)} | DT = {formatTime(time - prevTime)}.");
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
                    frameTime = formatTime(sw.ElapsedMilliseconds);
                    if (showInfoText) Console.WriteLine($"Simulation ended at: T = {frameTime}");

                    if (showInfoText) Console.WriteLine("Rendering canvas...");
                    this.BackColor = DrawingColor.Black;
                    render();
                    Invalidate();
                    if (saveFrames) saveToDownloads("almond");
                    if (saveWebp) gifFrames.Add((Bitmap)framebuffer.Clone());
                }
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
            catch (Exception ex)
            {
                Console.WriteLine("Program crashed:");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
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

            e.Graphics.DrawImage(
                framebuffer,
                new DrawingRectangle(0, 0, width, height));
        }

        float toDegrees(float radians)
        {
            return MathF.Abs(radians * 180f / MathF.PI) % 360 - 180;
        }

        void calcPen(int ci, int cj)
        {
            float theta0, theta1, omega0, omega1, v0, v1, prevWrapped;
            int flips;
            int index = ci + cj * size;
            float Wrap(float a) => MathF.Atan2(MathF.Sin(a), MathF.Cos(a));

            if (!doDeltaTime || !dpDataList[index].initialized)
            {
                theta0 = (ci - size / 2) * c;
                theta1 = (cj - size / 2) * c;

                if (!reletiveC)
                {
                    theta0 += centerX;
                    theta1 += centerY;
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
            }

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

                    if (delta > MathF.PI) flips--;
                    if (delta < -MathF.PI) flips++;

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
            float hue = flips * flipStrangth + wrapped * wrapedStrangth + MathF.PI;
            calcColor(hue, ci, cj);

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
            if (ms / 1000 < 60)
                convertedTime = $"{ms / 1000}s";
            else
            {
                float m = MathF.Floor((float)(ms / 1000 / 60));
                float s = MathF.Floor((float)(ms / 1000 % 60));
                convertedTime = $"{m};{s}m";
            }
            return convertedTime;
        }
    }
}