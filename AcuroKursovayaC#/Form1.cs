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
        private TileMap _tileMap;

        private VideoCapture _capture;
        private Mat _frame;
        private Bitmap _image1;
        private Mat _cameraMatrix;
        private Mat _distCoeffs;
        private float _markerLength = 0.1f; // �������� ������ ������� � ������
        private List<MarkerInfo> _markers = new List<MarkerInfo>(); // ������ ������������ ��������
        private Bitmap _mapImage; // �����
        private int _tileSize = 100; // ������ �����

        public Form1()
        {
            InitializeComponent();
            InitializeCameraParameters();
            _tileMap = new TileMap(256); // размер тайла 256x256

        }

        private void InitializeCameraParameters()
        {
            // ��������� ��������� ������ (�������� �� �������������!)
            double fx = trackBar1.Value; // �������� ����������
            double fy = trackBar1.Value;
            double cx = 320;  // ��� ���������� 640x480
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
            _capture = new VideoCapture(2); // ����������� ���������� ������ ������
            if (!_capture.IsOpened())
            {
                MessageBox.Show("�� ������� ������������ � ������!");
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
        // Нужно добавить метод для перспективной проекции:
        private Mat GetWarpedImage(Mat frame, Mat rvec, Mat tvec)
        {
            // Создаем матрицу гомографии
            Mat homography = new Mat();
            Cv2.Rodrigues(rvec, new Mat()); // преобразуем в матрицу вращения

            // Применяем перспективную трансформацию
            Point2f[] srcPoints = { new Point2f(0, 0), new Point2f(640, 0), new Point2f(640, 480), new Point2f(0, 480) };
            // Рассчитываем целевые точки на основе позиции камеры

            Mat perspectiveMatrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints);
            Mat warped = new Mat();
            Cv2.WarpPerspective(frame, warped, perspectiveMatrix, new OpenCvSharp.Size(800, 600));

            return warped;
        }
        //private void ProcessFrame()
        //{
        //    var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_100);
        //    DetectorParameters parameters = new DetectorParameters();
        //    parameters.CornerRefinementMethod = CornerRefineMethod.Subpix; // ��������� ��������

        //    CvAruco.DetectMarkers(_frame, dict, out var corners, out var ids, parameters, out _);

        //    if (ids == null || ids.Length == 0) return;

        //    var rvecs = new Mat();
        //    var tvecs = new Mat();
        //    // ������ ������� ��������
        //    CvAruco.EstimatePoseSingleMarkers(
        //        corners,
        //        _markerLength,
        //        _cameraMatrix,
        //        _distCoeffs,
        //        rvecs,
        //        tvecs
        //    );

        //    double minDistance = double.MaxValue;
        //    int closestMarkerId = -1;
        //    for (int i = 0; i < ids.Length; i++)
        //    {
        //        double distance = CalculateDistance(tvecs, i);

        //        if (distance < minDistance)
        //        {
        //            minDistance = distance;
        //            closestMarkerId = ids[i];
        //        }



        //    }

        //    if (closestMarkerId != -1 && minDistance <= 1)
        //    {
        //        label1.Text = $"Distance to closest marker (ID: {closestMarkerId}): {minDistance:F2} m {tvecs.At<int>(0)}";

        //    }

        //    // ��������� ���������� � ��������
        //    for (int i = 0; i < ids.Length; i++)
        //    {
        //        var markerCorners = corners[i].ToArray();
        //        //var center = CalculateMarkerCenter(markerCorners);
        //        var center = Convert3DToMapPosition(tvecs, i);
        //        DrawMarkerInfo(corners[i]);
        //        UpdateDistanceInfo(ids[i], tvecs, i);

        //        // ���������, ����� �� ������
        //        bool isNewMarker = true;
        //        foreach (var marker in _markers)
        //        {
        //            if (marker.Id == ids[i])
        //            {
        //                isNewMarker = false;
        //                break;
        //            }
        //        }

        //        if (isNewMarker)
        //        {
        //            // ��������� ����� ������ � ������
        //            _markers.Add(new MarkerInfo { Id = ids[i], Position = new OpenCvSharp.Point((int)center.X, (int)center.Y) });
        //            UpdateMap();
        //        }
        //    }
        //}
        // Исправленный метод обработки кадра:
        // Модифицируйте метод ProcessFrame:
        private void ProcessFrame()
        {
            var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_100);
            DetectorParameters parameters = new DetectorParameters();
            parameters.CornerRefinementMethod = CornerRefineMethod.Subpix;

            CvAruco.DetectMarkers(_frame, dict, out var corners, out var ids, parameters, out _);

            if (ids == null || ids.Length == 0) return;

            var rvecs = new Mat();
            var tvecs = new Mat();

            CvAruco.EstimatePoseSingleMarkers(
                corners,
                _markerLength,
                _cameraMatrix,
                _distCoeffs,
                rvecs,
                tvecs
            );

            // Обработка маркеров и добавление в тайловую карту
            for (int i = 0; i < ids.Length; i++)
            {
                // Преобразуем 3D координаты в мировые координаты карты
                double x = tvecs.At<double>(i, 0) * 100 + 400; // масштабирование
                double y = tvecs.At<double>(i, 2) * 100 + 300; // z координата как Y

                // Добавляем маркер в тайловую карту
                _tileMap.AddPoint(x, y, Scalar.Red, 5);

                // Добавляем ID маркера
                // Для текста нужно использовать дополнительный метод или OpenCV

                // Обновляем список маркеров
                bool isNewMarker = true;
                foreach (var marker in _markers)
                {
                    if (marker.Id == ids[i])
                    {
                        isNewMarker = false;
                        marker.Position = new OpenCvSharp.Point((int)x, (int)y);
                        break;
                    }
                }

                if (isNewMarker)
                {
                    _markers.Add(new MarkerInfo
                    {
                        Id = ids[i],
                        Position = new OpenCvSharp.Point((int)x, (int)y)
                    });
                }
            }

            // Если есть несколько маркеров, рисуем линии между ними
            if (_markers.Count > 1)
            {
                for (int i = 0; i < _markers.Count - 1; i++)
                {
                    _tileMap.AddLine(
                        _markers[i].Position.X, _markers[i].Position.Y,
                        _markers[i + 1].Position.X, _markers[i + 1].Position.Y,
                        Scalar.Blue, 2);
                }
            }

            UpdateMap();
        }

        // Модифицируйте UpdateMap:
        private void UpdateMap()
        {
            // Получаем полную карту из тайлов
            Mat fullMap = _tileMap.GetFullMap();

            if (fullMap.Width > 0 && fullMap.Height > 0)
            {
                // Рисуем маркеры на полной карте для отображения
                foreach (var marker in _markers)
                {
                    if (marker.Position.X >= 0 && marker.Position.Y >= 0)
                    {
                        Cv2.Circle(fullMap, marker.Position, 10, Scalar.Red, -1);
                        Cv2.PutText(fullMap, marker.Id.ToString(),
                            new OpenCvSharp.Point(marker.Position.X + 15, marker.Position.Y + 15),
                            HersheyFonts.HersheySimplex, 0.5, Scalar.Black, 1);
                    }
                }

                // Преобразуем в Bitmap для отображения
                _mapImage?.Dispose();
                _mapImage = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(fullMap);
                fullMap.Dispose();
            }
        }

        // Добавьте метод для сохранения тайлов:
        private void SaveTiles()
        {
            try
            {
                string tilesDirectory = Path.Combine(Application.StartupPath, "tiles");
                _tileMap.SaveTiles(tilesDirectory);
                MessageBox.Show($"Тайлы сохранены в папку: {tilesDirectory}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении тайлов: {ex.Message}");
            }
        }



        private double CalculateDistance(Mat tvecs, int index)
        {
            // ��������� ������ ����������� (tvec) ��� �������� �������
            double x = tvecs.At<double>(index, 0);
            double y = tvecs.At<double>(index, 1);
            double z = tvecs.At<double>(index, 2);

            // ��������� ��������� ���������� �� ������ �� �������
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
            // tvec[2] - ���������� �� ��� Z (� ������)
            double z = tvec.At<double>(index, 2);

            if (z > 0 && z < 1) // ����������� �������, ����������� �� �������
            {
                label2.Text = $"ID: {markerId}\nDistance: {z:F2} m";
            }
        }

        private OpenCvSharp.Point CalculateMarkerCenter(Point2f[] corners)
        {
            float x = 0, y = 0;
            foreach (var p in corners)
            {
                x += p.X;
                y += p.Y;
            }
            return new OpenCvSharp.Point((int)(x / 4), (int)(y / 4));
        }
        private OpenCvSharp.Point Convert3DToMapPosition(Mat tvec, int index)
        {
            // Используем tvec для получения реальных координат
            double x = tvec.At<double>(index, 0);
            double z = tvec.At<double>(index, 2); // z - глубина

            // Преобразуем в координаты карты
            int mapX = (int)(x * 100 + 400); // масштабирование и центрирование
            int mapY = (int)(z * 100 + 300);

            return new OpenCvSharp.Point(mapX, mapY);
        }
        private List<PictureBox> pictureBoxes = new List<PictureBox>();
        private int currentIndex = 0;
        private IEnumerable<Point2f> dstPoints;

        //private void UpdateMap()
        //{
        //    if (_mapImage == null)
        //    {
        //        _mapImage = new Bitmap(800, 600); // ������� �����
        //    }

        //    using (Graphics g = Graphics.FromImage(_mapImage))
        //    {
        //        g.Clear(Color.White); // ������� �����

        //        System.Drawing.Point[] points = new System.Drawing.Point[_markers.Count];
        //        for (int i = 0; i < _markers.Count; i++)
        //        {
        //            points[i] = new System.Drawing.Point(_markers[i].Position.X, _markers[i].Position.Y);
        //        }

        //        if (points.Length > 1)
        //        {
        //            g.DrawLines(Pens.Blue, points);
        //        }

        //        foreach (var marker in _markers)
        //        {
        //            if (marker.Position.X >= 0 && marker.Position.X < _mapImage.Width &&
        //                marker.Position.Y >= 0 && marker.Position.Y < _mapImage.Height)
        //            {
        //                if (marker.Id == 0)
        //                {
        //                    g.DrawRectangle(Pens.Blue, marker.Position.X - 10, marker.Position.Y - 10, 20, 20);
        //                    g.DrawString("�����", Font, Brushes.Black, new System.Drawing.Point(marker.Position.X - 20, marker.Position.Y - 20));
        //                }
        //                else if (marker.Id == 5)
        //                {
        //                    g.DrawRectangle(Pens.Green, marker.Position.X - 10, marker.Position.Y - 10, 20, 20);
        //                    g.DrawString("�����", Font, Brushes.Black, new System.Drawing.Point(marker.Position.X - 20, marker.Position.Y - 20));
        //                }
        //                else
        //                {
        //                    g.DrawRectangle(Pens.Red, marker.Position.X - 10, marker.Position.Y - 10, 20, 20);
        //                }
        //            }
        //        }
        //    }

        //    if (_mapImage != null)
        //    {
        //        PictureBox pictureBox = new PictureBox
        //        {
        //            Image = (Bitmap)_mapImage.Clone(),
        //            SizeMode = PictureBoxSizeMode.Zoom,
        //            Location = new System.Drawing.Point(10, 10 + pictureBox2.Height + 10 + (pictureBoxes.Count * 610)),
        //            Size = new System.Drawing.Size(800, 600)
        //        };

        //        pictureBoxes.Add(pictureBox);
        //        this.Controls.Add(pictureBox);
        //    }
        //}

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

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void SaveTile_Click(object sender, EventArgs e)
        {
            SaveTiles();
        }
    }

    public class MarkerInfo
    {
        public int Id { get; set; }
        public OpenCvSharp.Point Position { get; set; }
    }
}