using OpenCvSharp;
using OpenCvSharp.Aruco;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AcuroKursovayaC_
{
    public partial class Form1 : Form
    {
        private VideoCapture _capture;
        private Mat _frame;
        private Bitmap _image1;
        private Mat _cameraMatrix;
        private Mat _distCoeffs;
        private float _markerLength = 0.1f; // Реальный размер маркера в метрах
        private List<MarkerInfo> _markers = new List<MarkerInfo>(); // Список обнаруженных маркеров
        private Bitmap _mapImage; // Карта
        private int _tileSize = 100; // Размер тайла

        public Form1()
        {
            InitializeComponent();
            InitializeCameraParameters();
        }

        private void InitializeCameraParameters()
        {
            // Примерные параметры камеры (замените на калиброванные!)
            double fx = trackBar1.Value; // Фокусное расстояние
            double fy = trackBar1.Value;
            double cx = 320;  // Для разрешения 640x480
            double cy = 240;
            _cameraMatrix = new Mat(3, 3, MatType.CV_64FC1);
            _cameraMatrix.Set<double>(0, 0, fx);
            _cameraMatrix.Set<double>(0, 2, cx);
            _cameraMatrix.Set<double>(1, 1, fy);
            _cameraMatrix.Set<double>(1, 2, cy);

            _distCoeffs = new Mat(1, 5, MatType.CV_64FC1);
            _distCoeffs.Set<double>(0, 0, 0.05);
            _distCoeffs.Set<double>(0, 1, -0.1);
            _distCoeffs.Set<double>(0, 2, 0.001);
            _distCoeffs.Set<double>(0, 3, 0.001);
            _distCoeffs.Set<double>(0, 4, 0);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            _capture = new VideoCapture(2); // Используйте правильный индекс камеры
            if (!_capture.IsOpened())
            {
                MessageBox.Show("Не удалось подключиться к камере!");
                return;
            }

            _capture.FrameWidth = 640;
            _capture.FrameHeight = 480;
            _frame = new Mat();

            timer1.Interval = 33;
            timer1.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_capture == null || !_capture.IsOpened()) return;

            _capture.Read(_frame);
            if (_frame.Empty()) return;

            ProcessFrame();
            UpdateUI();
        }

        private void ProcessFrame()
        {
            var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_100);
            DetectorParameters parameters = new DetectorParameters();
            parameters.CornerRefinementMethod = CornerRefineMethod.Subpix; // Улучшение точности

            CvAruco.DetectMarkers(_frame, dict, out var corners, out var ids, parameters, out _);

            if (ids == null || ids.Length == 0) return;

            var rvecs = new Mat();
            var tvecs = new Mat();
            // Оценка позиции маркеров
            CvAruco.EstimatePoseSingleMarkers(
                corners,
                _markerLength,
                _cameraMatrix,
                _distCoeffs,
                rvecs,
                tvecs
            );

            double minDistance = double.MaxValue;
            int closestMarkerId = -1;
            for (int i = 0; i < ids.Length; i++)
            {
                double distance = CalculateDistance(tvecs, i);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestMarkerId = ids[i];
                }



            }

            if (closestMarkerId != -1 && minDistance <= 1)
            {
                label1.Text = $"Distance to closest marker (ID: {closestMarkerId}): {minDistance:F2} m {tvecs.At<int>(0)}";

            }

            // Обновляем информацию о маркерах
            for (int i = 0; i < ids.Length; i++)
            {
                var markerCorners = corners[i].ToArray();
                var center = CalculateMarkerCenter(markerCorners);
                DrawMarkerInfo(corners[i]);
                UpdateDistanceInfo(ids[i], tvecs, i);

                // Проверяем, новый ли маркер
                bool isNewMarker = true;
                foreach (var marker in _markers)
                {
                    if (marker.Id == ids[i])
                    {
                        isNewMarker = false;
                        break;
                    }
                }

                if (isNewMarker)
                {
                    // Добавляем новый маркер в список
                    _markers.Add(new MarkerInfo { Id = ids[i], Position = new OpenCvSharp.Point((int)center.X, (int)center.Y) });
                    UpdateMap();
                }
            }
        }

        private double CalculateDistance(Mat tvecs, int index)
        {
            // Извлекаем вектор перемещения (tvec) для текущего маркера
            double x = tvecs.At<double>(index, 0);
            double y = tvecs.At<double>(index, 1);
            double z = tvecs.At<double>(index, 2);

            // Вычисляем евклидово расстояние от камеры до маркера
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private void DrawMarkerInfo(Point2f[] corners)
        {
            foreach (var corner in corners)
            {
                Cv2.Circle(_frame, (OpenCvSharp.Point)corner, 10, Scalar.Red, -1);
            }
        }

        private void UpdateDistanceInfo(int markerId, Mat tvec, int index)
        {
            // tvec[2] - расстояние по оси Z (в метрах)
            double z = tvec.At<double>(index, 2);

            if (z > 0 && z < 1) // Отбрасываем маркеры, находящиеся за камерой
            {
                label2.Text = $"ID: {markerId}\nDistance: {z:F2} m";
            }
        }

        private OpenCvSharp.Point CalculateMarkerCenter(Point2f[] corners)
        {
            // Вычисляем среднее арифметическое всех углов
            float x = 0, y = 0;
            foreach (var p in corners)
            {
                x += p.X;
                y += p.Y;
            }
            return new OpenCvSharp.Point((int)(x / 4), (int)(y / 4));
        }
        private List<PictureBox> pictureBoxes = new List<PictureBox>();
        private int currentIndex = 0;

        private void UpdateMap()
        {
            if (_mapImage == null)
            {
                _mapImage = new Bitmap(800, 600); // Создаем карту
            }

            using (Graphics g = Graphics.FromImage(_mapImage))
            {
                g.Clear(Color.White); // Очищаем карту

                System.Drawing.Point[] points = new System.Drawing.Point[_markers.Count];
                for (int i = 0; i < _markers.Count; i++)
                {
                    points[i] = new System.Drawing.Point(_markers[i].Position.X, _markers[i].Position.Y);
                }

                if (points.Length > 1)
                {
                    g.DrawLines(Pens.Blue, points);
                }

                foreach (var marker in _markers)
                {
                    if (marker.Position.X >= 0 && marker.Position.X < _mapImage.Width &&
                        marker.Position.Y >= 0 && marker.Position.Y < _mapImage.Height)
                    {
                        if (marker.Id == 0)
                        {
                            g.DrawRectangle(Pens.Blue, marker.Position.X - 10, marker.Position.Y - 10, 20, 20);
                            g.DrawString("Старт", Font, Brushes.Black, new System.Drawing.Point(marker.Position.X - 20, marker.Position.Y - 20));
                        }
                        else if (marker.Id == 5)
                        {
                            g.DrawRectangle(Pens.Green, marker.Position.X - 10, marker.Position.Y - 10, 20, 20);
                            g.DrawString("Финиш", Font, Brushes.Black, new System.Drawing.Point(marker.Position.X - 20, marker.Position.Y - 20));
                        }
                        else
                        {
                            g.DrawRectangle(Pens.Red, marker.Position.X - 10, marker.Position.Y - 10, 20, 20);
                        }
                    }
                }
            }

            if (_mapImage != null)
            {
                PictureBox pictureBox = new PictureBox
                {
                    Image = (Bitmap)_mapImage.Clone(),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new System.Drawing.Point(10, 10 + pictureBox2.Height + 10 + (pictureBoxes.Count * 610)),
                    Size = new System.Drawing.Size(800, 600)
                };

                pictureBoxes.Add(pictureBox);
                this.Controls.Add(pictureBox);
            }
        }

        private void UpdateUI()
        {
            if (_image1 != null)
            {
                _image1.Dispose();
            }

            _image1 = _frame.ToBitmap();
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = _image1;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;

            if (_mapImage != null)
            {
                pictureBox2.Image?.Dispose();
                pictureBox2.Image = (Bitmap)_mapImage.Clone();
                pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            timer1.Stop();
            _capture?.Dispose();
            _frame?.Dispose();
            _cameraMatrix?.Dispose();
            _distCoeffs?.Dispose();
        }

        private void trackBar1_ValueChanged(object sender, EventArgs e)
        {
            InitializeCameraParameters();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (currentIndex > 0)
            {
                currentIndex--;
            }

            foreach (var pictureBox in pictureBoxes)
            {
                pictureBox.Visible = false;
            }

            pictureBoxes[currentIndex].Visible = true;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (currentIndex < pictureBoxes.Count - 1)
            {
                currentIndex++;
            }

            foreach (var pictureBox in pictureBoxes)
            {
                pictureBox.Visible = false;
            }

            pictureBoxes[currentIndex].Visible = true;
        }
    }

    public class MarkerInfo
    {
        public int Id { get; set; }
        public OpenCvSharp.Point Position { get; set; }
    }
}