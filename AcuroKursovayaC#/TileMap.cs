using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;

namespace AcuroKursovayaC_
{
    public class TileMap
    {
        private Dictionary<string, Mat> _tiles = new Dictionary<string, Mat>();
        private int _tileSize;
        private object _lockObject = new object();

        public TileMap(int tileSize = 256)
        {
            _tileSize = tileSize;
        }

        /// <summary>
        /// Получить ключ тайла по координатам
        /// </summary>
        private string GetTileKey(int tileX, int tileY)
        {
            return $"{tileX}_{tileY}";
        }

        /// <summary>
        /// Получить индексы тайла по мировым координатам
        /// </summary>
        public (int tileX, int tileY) GetTileIndices(double worldX, double worldY)
        {
            int tileX = (int)Math.Floor(worldX / _tileSize);
            int tileY = (int)Math.Floor(worldY / _tileSize);
            return (tileX, tileY);
        }

        /// <summary>
        /// Получить локальные координаты внутри тайла
        /// </summary>
        public (int localX, int localY) GetLocalCoordinates(double worldX, double worldY)
        {
            int localX = (int)(worldX % _tileSize);
            int localY = (int)(worldY % _tileSize);
            if (localX < 0) localX += _tileSize;
            if (localY < 0) localY += _tileSize;
            return (localX, localY);
        }

        /// <summary>
        /// Убедиться, что тайл существует
        /// </summary>
        private void EnsureTileExists(int tileX, int tileY)
        {
            string key = GetTileKey(tileX, tileY);
            if (!_tiles.ContainsKey(key))
            {
                _tiles[key] = new Mat(_tileSize, _tileSize, MatType.CV_8UC3, Scalar.White);
            }
        }

        /// <summary>
        /// Добавить точку на карту
        /// </summary>
        public void AddPoint(double worldX, double worldY, Scalar color, int radius = 3)
        {
            lock (_lockObject)
            {
                var (tileX, tileY) = GetTileIndices(worldX, worldY);
                var (localX, localY) = GetLocalCoordinates(worldX, worldY);

                EnsureTileExists(tileX, tileY);

                string key = GetTileKey(tileX, tileY);
                Cv2.Circle(_tiles[key], new OpenCvSharp.Point(localX, localY), radius, color, -1);
            }
        }

        /// <summary>
        /// Добавить линию на карту
        /// </summary>
        public void AddLine(double startX, double startY, double endX, double endY, Scalar color, int thickness = 2)
        {
            lock (_lockObject)
            {
                // Простая реализация - рисуем линию в соответствующих тайлах
                var (startTileX, startTileY) = GetTileIndices(startX, startY);
                var (endTileX, endTileY) = GetTileIndices(endX, endY);

                // Для простоты рисуем в начальном тайле
                var (localStartX, localStartY) = GetLocalCoordinates(startX, startY);
                var (localEndX, localEndY) = GetLocalCoordinates(endX, endY);

                EnsureTileExists(startTileX, startTileY);
                string key = GetTileKey(startTileX, startTileY);

                Cv2.Line(_tiles[key],
                    new OpenCvSharp.Point(localStartX, localStartY),
                    new OpenCvSharp.Point(localEndX, localEndY),
                    color, thickness);
            }
        }

        /// <summary>
        /// Добавить изображение в тайл
        /// </summary>
        public void AddImageToTile(double worldX, double worldY, Mat image)
        {
            lock (_lockObject)
            {
                var (tileX, tileY) = GetTileIndices(worldX, worldY);
                var (localX, localY) = GetLocalCoordinates(worldX, worldY);

                EnsureTileExists(tileX, tileY);
                string key = GetTileKey(tileX, tileY);

                // Ограничиваем координаты размером тайла
                int pasteX = Math.Max(0, Math.Min(localX, _tileSize - image.Width));
                int pasteY = Math.Max(0, Math.Min(localY, _tileSize - image.Height));

                // Создаем ROI для вставки
                var roi = new Rect(pasteX, pasteY,
                    Math.Min(image.Width, _tileSize - pasteX),
                    Math.Min(image.Height, _tileSize - pasteY));

                if (roi.Width > 0 && roi.Height > 0)
                {
                    var tileRoi = new Mat(_tiles[key], roi);
                    var imageRoi = new Mat(image, new Rect(0, 0, roi.Width, roi.Height));
                    imageRoi.CopyTo(tileRoi);
                }
            }
        }

        /// <summary>
        /// Получить тайл по индексам
        /// </summary>
        public Mat GetTile(int tileX, int tileY)
        {
            string key = GetTileKey(tileX, tileY);
            return _tiles.ContainsKey(key) ? _tiles[key].Clone() : null;
        }

        /// <summary>
        /// Сохранить все тайлы в файлы
        /// </summary>
        public void SaveTiles(string directoryPath)
        {
            lock (_lockObject)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                foreach (var kvp in _tiles)
                {
                    string filePath = Path.Combine(directoryPath, $"tile_{kvp.Key}.png");
                    Cv2.ImWrite(filePath, kvp.Value);
                }
            }
        }

        /// <summary>
        /// Получить список всех тайлов с их координатами
        /// </summary>
        public List<(int tileX, int tileY, Mat tileImage)> GetAllTiles()
        {
            lock (_lockObject)
            {
                var result = new List<(int, int, Mat)>();
                foreach (var kvp in _tiles)
                {
                    var parts = kvp.Key.Split('_');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int tileX) &&
                        int.TryParse(parts[1], out int tileY))
                    {
                        result.Add((tileX, tileY, kvp.Value.Clone()));
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Очистить карту
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                foreach (var tile in _tiles.Values)
                {
                    tile?.Dispose();
                }
                _tiles.Clear();
            }
        }

        /// <summary>
        /// Получить границы карты (минимальные и максимальные индексы тайлов)
        /// </summary>
        public ((int minX, int minY), (int maxX, int maxY)) GetMapBounds()
        {
            lock (_lockObject)
            {
                if (_tiles.Count == 0)
                    return ((0, 0), (0, 0));

                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;

                foreach (var kvp in _tiles)
                {
                    var parts = kvp.Key.Split('_');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int tileX) &&
                        int.TryParse(parts[1], out int tileY))
                    {
                        minX = Math.Min(minX, tileX);
                        minY = Math.Min(minY, tileY);
                        maxX = Math.Max(maxX, tileX);
                        maxY = Math.Max(maxY, tileY);
                    }
                }

                return ((minX, minY), (maxX, maxY));
            }
        }

        /// <summary>
        /// Создать полную карту из всех тайлов (для визуализации)
        /// </summary>
        public Mat GetFullMap()
        {
            lock (_lockObject)
            {
                var bounds = GetMapBounds();
                var (minX, minY) = bounds.Item1;
                var (maxX, maxY) = bounds.Item2;

                int width = (maxX - minX + 1) * _tileSize;
                int height = (maxY - minY + 1) * _tileSize;

                if (width <= 0 || height <= 0)
                    return new Mat(100, 100, MatType.CV_8UC3, Scalar.White);

                Mat fullMap = new Mat(height, width, MatType.CV_8UC3, Scalar.White);

                for (int tileX = minX; tileX <= maxX; tileX++)
                {
                    for (int tileY = minY; tileY <= maxY; tileY++)
                    {
                        string key = GetTileKey(tileX, tileY);
                        if (_tiles.ContainsKey(key))
                        {
                            int x = (tileX - minX) * _tileSize;
                            int y = (tileY - minY) * _tileSize;

                            var roi = new Rect(x, y, _tileSize, _tileSize);
                            var tileRoi = new Mat(fullMap, roi);
                            _tiles[key].CopyTo(tileRoi);
                        }
                    }
                }

                return fullMap;
            }
        }
    }
}
