using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection.Metadata;
using System.Windows.Forms;
using System.IO;

namespace almond
{
    public partial class Form1 : Form
    {
        //float theta0;
        //float theta1;
        //float omega0;
        //float omega1;
        float c = 0.005f;
        bool reletiveC = true;
        bool saveImage = true;
        bool skipRenderWait = true;
        bool damping = true;
        bool showInfoText = false;
        int size = 250;

        float simulationTime = 5.5f;
        float timeStep = 0.01f;

        float l0 = 1f;
        float l1 = 1f;
        float m0 = 1f;
        float m1 = 1f;
        float g = 9.81f;

        float centerX = 0f;
        float centerY = 0f;

        int width = 1;
        int height = 1;

        String frameTime;
        float subStep;

        //int jDevider = 250;
        //int printDevider = -1;

        List<float> theta0list;
        Color[] pixels;
        Bitmap framebuffer;

        public Form1()
        {
            try
            {
                InitializeComponent();

                for (int loop = 0; loop < 10; loop++)
                {
                    l1 = loop;
                    width = size;
                    height = size;

                    pixels = new Color[width * height];
                    framebuffer = new Bitmap(width, height);

                    this.ClientSize = new Size(width, height);

                    if (reletiveC) c = MathF.PI * 2 / size;
                    subStep = timeStep / 10;

                    //setPixel(10, 10, Color.Red);
                    var options = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount / 2
                    };
                    //int k = 0;
                    int localSize = size;
                    int logStep = (int)(localSize / 10);

                    theta0list = new List<float>(localSize * localSize);

                    var sw = Stopwatch.StartNew();
                    Console.WriteLine($"Starting multythred simulation {loop}.");
                    Parallel.For(0, localSize, options, i =>
                    {
                        for (int j = 0; j < localSize; j++)
                        {
                            calcPen(i, j);
                        }
                        if (i % logStep == 0 && showInfoText) Console.WriteLine($"{i} of {size} rows rendering at: T = {sw.ElapsedMilliseconds}ms");
                    });
                    sw.Stop();
                    if (sw.ElapsedMilliseconds / 1000 < 60)
                        frameTime = $"{sw.ElapsedMilliseconds / 1000}s";
                    else
                    {
                        float m = MathF.Floor((float)(sw.ElapsedMilliseconds / 1000 / 60));
                        float s = MathF.Floor((float)(sw.ElapsedMilliseconds / 1000 % 60));
                        frameTime = $"{m}-{s}m";
                    }
                    if (showInfoText) Console.WriteLine($"Simulation ended at: T = {frameTime}");

                    if (!skipRenderWait)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            Console.WriteLine($"Rendering in {i * -1 + 3}...");
                            Thread.Sleep(1000);
                        }
                    }

                    Console.WriteLine("Rendering canvas...");
                    this.BackColor = Color.Black;
                    render();
                    Invalidate();
                    if (saveImage) saveToDownloads("almond");
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

        public void setPixel(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            pixels[x + y * width] = c;
        }

        void render()
        {
            var data = framebuffer.LockBits(
                new Rectangle(0, 0, width, height),
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
                new Rectangle(0, 0, width, height));
        }

        float toDegrees(float radians)
        {
            return MathF.Abs(radians * 180f / MathF.PI) % 360 - 180;
        }

        void calcPen(int ci, int cj)
        {
            float theta0 = (ci - size / 2) * c;
            float theta1 = (cj - size / 2) * c;
            if (!reletiveC)
            {
                theta0 += centerX;
                theta1 += centerY;
            }

            float omega0 = 0;
            float omega1 = 0;

            float v0 = 0;
            float v1 = 0;

            int flips = 0;

            float localSubStep = subStep;

            float Wrap(float a) => MathF.Atan2(MathF.Sin(a), MathF.Cos(a));

            float prevWrapped = Wrap(theta0);

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
            float hue = flips * 0.02f + wrapped * 0.5f + MathF.PI;
            calcColor(hue, ci, cj);
        }

        void calcColor(float ang, int x, int y)
        {
            float cr = (MathF.Sin(ang) + 1f) * 0.5f;
            float cg = (MathF.Sin(ang + 2f * MathF.PI / 3f) + 1f) * 0.5f;
            float cb = (MathF.Sin(ang + 4f * MathF.PI / 3f) + 1f) * 0.5f;
            setPixel(x, y, Color.FromArgb((int)(cr * 255), (int)(cg * 255), (int)(cb * 255)));
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
    }
}