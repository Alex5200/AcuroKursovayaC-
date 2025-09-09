using OpenCvSharp;
using OpenCvSharp.Aruco;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        private float _markerLength = 0.15f; // Размер маркера 15 см
        private List<MarkerInfo> _markers = new List<MarkerInfo>();
        private System.Windows.Forms.Timer _timer;
        private Mat _mapImage;
        private List<System.Drawing.Point> _trajectory = new List<System.Drawing.Point>();
        private System.Drawing.Point _lastRobotPosition = new System.Drawing.Point(300, 300);
        private Dictionary<string, Mat> _tiles = new Dictionary<string, Mat>();
        private int _tileSize = 600;
        private bool _mapInitialized = false;
        private const double CAMERA_ANGLE = 90.0; // Камера смотрит строго вниз
        private const double CAMERA_HEIGHT = 2.0; // Высота камеры над полом в метрах

        public Form1()
        {
            InitializeComponent();
            InitializeCameraParameters();
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 33;
            _timer.Tick += Timer1_Tick;
            InitializeMap();
        }

        private void InitializeMap()
        {
            // Создаем начальный тайл
            string key = "0_0";
            _tiles[key] = new Mat(_tileSize, _tileSize, MatType.CV_8UC3, new Scalar(240, 240, 240));
            _mapInitialized = true;
        }

        private void InitializeCameraParameters()
        {
            // Параметры камеры для вертикального обзора
            double fx = 800;
            double fy = 800;
            double cx = 320;
            double cy = 240;

            _cameraMatrix = Mat.Eye(3, 3, MatType.CV_64FC1);
            _cameraMatrix.Set<double>(0, 0, fx);
            _cameraMatrix.Set<double>(0, 2, cx);
            _cameraMatrix.Set<double>(1, 1, fy);
            _cameraMatrix.Set<double>(1, 2, cy);

            _distCoeffs = Mat.Zeros(1, 5, MatType.CV_64FC1);
            _distCoeffs.Set<double>(0, 0, 0.05);
            _distCoeffs.Set<double>(0, 1, -0.1);
            _distCoeffs.Set<double>(0, 2, 0.001);
            _distCoeffs.Set<double>(0, 3, 0.001);
            _distCoeffs.Set<double>(0, 4, 0);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (_capture?.IsOpened() == true)
            {
                _timer.Stop();
                _capture.Release();
                _capture.Dispose();
                btnStart.Text = "Start";
                return;
            }

            _capture = new VideoCapture(0);
            if (!_capture.IsOpened())
            {
                _capture?.Dispose();
                _capture = new VideoCapture(1);
                if (!_capture.IsOpened())
                {
                    _capture?.Dispose();
                    _capture = new VideoCapture(2);
                    if (!_capture.IsOpened())
                    {
                        MessageBox.Show("Не удалось подключиться ни к одной камере!");
                        return;
                    }
                }
            }

            _capture.FrameWidth = 640;
            _capture.FrameHeight = 480;
            _frame = new Mat();

            _timer.Start();
            btnStart.Text = "Stop";
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            if (_capture?.IsOpened() != true) return;

            if (_capture.Read(_frame) && !_frame.Empty())
            {
                ProcessFrame();
                UpdateUI();
            }
        }

        private void ProcessFrame()
        {
            var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_100);
            var parameters = new DetectorParameters();
            parameters.CornerRefinementMethod = CornerRefineMethod.Subpix;

            Point2f[][] corners;
            int[] ids;
            Point2f[][] rejectedImgPoints;

            CvAruco.DetectMarkers(_frame, dict, out corners, out ids, parameters, out rejectedImgPoints);

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

            ProcessMarkersAndRobotPosition(ids, tvecs, corners, rvecs);

            rvecs.Dispose();
            tvecs.Dispose();
        }

        private void ProcessMarkersAndRobotPosition(int[] ids, Mat tvecs, Point2f[][] corners, Mat rvecs)
        {
            // Находим ближайший маркер для определения позиции робота
            int closestMarkerIndex = 0;
            double minDistance = double.MaxValue;

            for (int i = 0; i < ids.Length; i++)
            {
                double x = tvecs.At<double>(i, 0);
                double y = tvecs.At<double>(i, 1);
                double z = tvecs.At<double>(i, 2);

                // При вертикальной камере Z - глубина, X и Y - горизонтальные координаты
                double distance = Math.Abs(z); // Расстояние по глубине

                if (distance < minDistance && distance > 0)
                {
                    minDistance = distance;
                    closestMarkerIndex = i;
                }
            }

            // Определяем позицию робота на основе ближайшего маркера
            if (ids.Length > 0 && minDistance < 5.0) // Ограничиваем расстояние
            {
                // Получаем координаты ближайшего маркера
                double markerX = tvecs.At<double>(closestMarkerIndex, 0); // Поперечная координата
                double markerY = tvecs.At<double>(closestMarkerIndex, 1); // Вертикальная координата  
                double markerZ = tvecs.At<double>(closestMarkerIndex, 2); // Глубина (расстояние до камеры)

                // При вертикальной камере:
                // X - смещение влево/вправо
                // Y - смещение вверх/вниз  
                // Z - расстояние от камеры

                // Преобразуем в координаты карты (с центрированием)
                // Учитываем, что камера смотрит сверху, поэтому X и Z определяют положение на полу
                int mapX = (int)(markerX * 300 + 300); // Масштаб: 1м = 300px, центр в (300,300)
                int mapY = (int)(-markerZ * 300 + 300); // Минус потому что Z направлен от камеры

                System.Drawing.Point robotPosition = new System.Drawing.Point(mapX, mapY);

                // Обновляем траекторию
                if (_trajectory.Count == 0 ||
                    DistanceBetweenPoints(robotPosition, _lastRobotPosition) > 5)
                {
                    _trajectory.Add(robotPosition);
                    _lastRobotPosition = robotPosition;

                    // Добавляем точку на карту (синяя точка для робота)
                    AddPointToMap(mapX, mapY, new Scalar(255, 100, 100), 5);
                }
            }

            // Обработка каждого маркера
            for (int i = 0; i < ids.Length; i++)
            {
                // Получаем координаты маркера
                double markerX = tvecs.At<double>(i, 0);
                double markerY = tvecs.At<double>(i, 1);
                double markerZ = tvecs.At<double>(i, 2);

                // Преобразуем в координаты карты
                int mapX = (int)(markerX * 300 + 300);
                int mapY = (int)(-markerZ * 300 + 300); // Минус для правильной ориентации

                // Визуализация на кадре
                VisualizeMarker(corners[i], ids[i]);

                // Добавляем маркер в систему отслеживания
                bool markerExists = false;
                int markerIndex = -1;

                for (int j = 0; j < _markers.Count; j++)
                {
                    if (_markers[j].Id == ids[i])
                    {
                        markerExists = true;
                        markerIndex = j;
                        break;
                    }
                }

                if (!markerExists)
                {
                    _markers.Add(new MarkerInfo
                    {
                        Id = ids[i],
                        Position = new OpenCvSharp.Point(mapX, mapY),
                        WorldPosition = new System.Drawing.PointF((float)markerX, (float)(-markerZ))
                    });

                    // Добавляем маркер на карту (красная точка)
                    AddPointToMap(mapX, mapY, new Scalar(0, 0, 255), 7);
                }
                else if (markerIndex >= 0)
                {
                    // Обновляем позицию если она изменилась значительно
                    var oldPos = _markers[markerIndex].Position;
                    double distance = Math.Sqrt(
                        Math.Pow(oldPos.X - mapX, 2) +
                        Math.Pow(oldPos.Y - mapY, 2));

                    if (distance > 8)
                    {
                        _markers[markerIndex].Position = new OpenCvSharp.Point(mapX, mapY);
                        _markers[markerIndex].WorldPosition = new System.Drawing.PointF((float)markerX, (float)(-markerZ));
                    }
                }
            }

            // Рисуем линии траектории на карте
            for (int i = 1; i < _trajectory.Count; i++)
            {
                AddLineToMap(
                    _trajectory[i - 1].X, _trajectory[i - 1].Y,
                    _trajectory[i].X, _trajectory[i].Y,
                    new Scalar(0, 200, 0), 3);
            }
        }

        private double DistanceBetweenPoints(System.Drawing.Point p1, System.Drawing.Point p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }

        private void VisualizeMarker(Point2f[] corners, int markerId)
        {
            // Рисуем контур маркера
            for (int i = 0; i < corners.Length; i++)
            {
                var pt1 = new OpenCvSharp.Point(corners[i].X, corners[i].Y);
                var pt2 = new OpenCvSharp.Point(
                    corners[(i + 1) % corners.Length].X,
                    corners[(i + 1) % corners.Length].Y);
                Cv2.Line(_frame, pt1, pt2, new Scalar(0, 255, 0), 2);
            }

            // Рисуем углы
            for (int i = 0; i < corners.Length; i++)
            {
                Cv2.Circle(_frame, new OpenCvSharp.Point(corners[i].X, corners[i].Y), 4, new Scalar(0, 0, 255), -1);
            }

            // Добавляем ID маркера
            if (corners.Length > 0)
            {
                float centerX = 0, centerY = 0;
                foreach (var point in corners)
                {
                    centerX += point.X;
                    centerY += point.Y;
                }
                centerX /= corners.Length;
                centerY /= corners.Length;

                Cv2.PutText(_frame, markerId.ToString(),
                    new OpenCvSharp.Point(centerX, centerY - 20),
                    HersheyFonts.HersheySimplex, 0.8, new Scalar(255, 0, 0), 2);
            }
        }

        private void AddPointToMap(int x, int y, Scalar color, int radius)
        {
            try
            {
                // Используем основной тайл
                string key = "0_0";
                if (!_tiles.ContainsKey(key))
                {
                    _tiles[key] = new Mat(_tileSize, _tileSize, MatType.CV_8UC3, new Scalar(240, 240, 240));
                }

                // Проверяем границы
                if (x >= 0 && x < _tileSize && y >= 0 && y < _tileSize)
                {
                    Cv2.Circle(_tiles[key], new OpenCvSharp.Point(x, y), radius, color, -1);

                    // Добавляем контур для лучшей видимости
                    if (radius > 3)
                    {
                        Cv2.Circle(_tiles[key], new OpenCvSharp.Point(x, y), radius + 1, new Scalar(0, 0, 0), 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка добавления точки на карту: {ex.Message}");
            }
        }

        private void AddLineToMap(int startX, int startY, int endX, int endY, Scalar color, int thickness)
        {
            try
            {
                string key = "0_0";
                if (_tiles.ContainsKey(key))
                {
                    // Проверяем границы
                    if (startX >= 0 && startX < _tileSize && startY >= 0 && startY < _tileSize &&
                        endX >= 0 && endX < _tileSize && endY >= 0 && endY < _tileSize)
                    {
                        Cv2.Line(_tiles[key],
                            new OpenCvSharp.Point(startX, startY),
                            new OpenCvSharp.Point(endX, endY),
                            color, thickness);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка добавления линии на карту: {ex.Message}");
            }
        }

        private void UpdateUI()
        {
            try
            {
                // Обновляем видео с камеры
                _image1?.Dispose();
                _image1 = _frame.ToBitmap();

                pictureBox1.Image?.Dispose();
                pictureBox1.Image = _image1;
                pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;

                // Обновляем карту
                UpdateMapImage();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления UI: {ex.Message}");
            }
        }

        private void UpdateMapImage()
        {
            try
            {
                string key = "0_0";
                if (_tiles.ContainsKey(key))
                {
                    // Создаем копию для отображения
                    var displayMap = _tiles[key].Clone();

                    // Добавляем сетку для лучшей ориентации
                    DrawGrid(displayMap);

                    // Добавляем маркеры поверх тайлов
                    foreach (var marker in _markers)
                    {
                        if (marker.Position.X >= 0 && marker.Position.X < _tileSize &&
                            marker.Position.Y >= 0 && marker.Position.Y < _tileSize)
                        {
                            // Красный круг для маркера
                            Cv2.Circle(displayMap, marker.Position, 10, new Scalar(0, 0, 255), -1);
                            Cv2.Circle(displayMap, marker.Position, 12, new Scalar(0, 0, 0), 2);

                            // ID маркера
                            Cv2.PutText(displayMap, marker.Id.ToString(),
                                new OpenCvSharp.Point(marker.Position.X + 15, marker.Position.Y - 15),
                                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 0), 2);
                        }
                    }

                    // Рисуем траекторию
                    if (_trajectory.Count > 1)
                    {
                        var points = new OpenCvSharp.Point[_trajectory.Count];
                        for (int i = 0; i < _trajectory.Count; i++)
                        {
                            points[i] = new OpenCvSharp.Point(_trajectory[i].X, _trajectory[i].Y);
                        }
                        Cv2.Polylines(displayMap, new[] { points }, false, new Scalar(0, 200, 0), 3);
                    }

                    // Рисуем текущую позицию робота
                    if (_trajectory.Count > 0)
                    {
                        var currentPos = _trajectory[_trajectory.Count - 1];
                        if (currentPos.X >= 0 && currentPos.X < _tileSize &&
                            currentPos.Y >= 0 && currentPos.Y < _tileSize)
                        {
                            // Синий треугольник для робота
                            var points = new OpenCvSharp.Point[]
                            {
                                new OpenCvSharp.Point(currentPos.X, currentPos.Y - 10),
                                new OpenCvSharp.Point(currentPos.X - 8, currentPos.Y + 8),
                                new OpenCvSharp.Point(currentPos.X + 8, currentPos.Y + 8)
                            };
                            Cv2.FillConvexPoly(displayMap, points, new Scalar(255, 100, 100));
                            Cv2.Polylines(displayMap, new[] { points }, true, new Scalar(0, 0, 0), 2);
                        }
                    }

                    var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(displayMap);
                    displayMap.Dispose();

                    pictureBox2.Image?.Dispose();
                    pictureBox2.Image = bitmap;
                    pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления карты: {ex.Message}");
            }
        }

        private void DrawGrid(Mat map)
        {
            try
            {
                int gridSize = 50; // 50 пикселей = ~16.7 см при масштабе 1м = 300px
                Scalar gridColor = new Scalar(200, 200, 200);

                // Вертикальные линии
                for (int x = 0; x < map.Width; x += gridSize)
                {
                    Cv2.Line(map, new OpenCvSharp.Point(x, 0), new OpenCvSharp.Point(x, map.Height), gridColor, 1);
                }

                // Горизонтальные линии
                for (int y = 0; y < map.Height; y += gridSize)
                {
                    Cv2.Line(map, new OpenCvSharp.Point(0, y), new OpenCvSharp.Point(map.Width, y), gridColor, 1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка рисования сетки: {ex.Message}");
            }
        }

        private void SaveTiles()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string mapDirectory = Path.Combine(Application.StartupPath, $"robot_map_{timestamp}");

                if (!Directory.Exists(mapDirectory))
                {
                    Directory.CreateDirectory(mapDirectory);
                }

                foreach (var kvp in _tiles)
                {
                    try
                    {
                        string filePath = Path.Combine(mapDirectory, $"map_tile_{kvp.Key}.png");
                        Cv2.ImWrite(filePath, kvp.Value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка сохранения тайла {kvp.Key}: {ex.Message}");
                    }
                }

                SaveMarkerInfo(mapDirectory);

                MessageBox.Show($"Карта сохранена в папку: {mapDirectory}",
                    "Сохранение карты", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения карты: {ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveMarkerInfo(string directory)
        {
            try
            {
                string infoFile = Path.Combine(directory, "markers_info.txt");
                using (var writer = new StreamWriter(infoFile))
                {
                    writer.WriteLine("Информация о маркерах:");
                    writer.WriteLine("====================");
                    writer.WriteLine($"Всего маркеров: {_markers.Count}");
                    writer.WriteLine($"Угол камеры: {CAMERA_ANGLE} градусов");
                    writer.WriteLine($"Высота камеры: {CAMERA_HEIGHT} м");
                    writer.WriteLine();

                    foreach (var marker in _markers)
                    {
                        writer.WriteLine($"ID: {marker.Id}");
                        writer.WriteLine($"Координаты на карте: ({marker.Position.X}, {marker.Position.Y})");
                        writer.WriteLine($"Мировые координаты: ({marker.WorldPosition.X:F3}, {marker.WorldPosition.Y:F3})");
                        writer.WriteLine("---");
                    }

                    writer.WriteLine();
                    writer.WriteLine("Траектория робота:");
                    writer.WriteLine("=================");
                    for (int i = 0; i < _trajectory.Count; i++)
                    {
                        writer.WriteLine($"Точка {i + 1}: ({_trajectory[i].X}, {_trajectory[i].Y})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения информации о маркерах: {ex.Message}");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _timer?.Stop();
            SaveTiles();

            _capture?.Release();
            _capture?.Dispose();
            _frame?.Dispose();
            _cameraMatrix?.Dispose();
            _distCoeffs?.Dispose();
            _image1?.Dispose();

            foreach (var tile in _tiles.Values)
            {
                tile?.Dispose();
            }
            _tiles.Clear();
        }

        private void trackBar1_ValueChanged(object sender, EventArgs e)
        {
            // Игнорируем для упрощения
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Пустой обработчик
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // Пустой обработчик
        }

        private void label1_Click(object sender, EventArgs e)
        {
            // Пустой обработчик
        }

        private void label2_Click(object sender, EventArgs e)
        {
            // Пустой обработчик
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
        public System.Drawing.PointF WorldPosition { get; set; }
    }
}