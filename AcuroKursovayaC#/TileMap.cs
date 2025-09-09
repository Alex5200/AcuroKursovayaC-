using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;

namespace AcuroKursovayaC_
{
    public class TileMap : IDisposable
    {
        private Dictionary<string, Mat> _tiles = new Dictionary<string, Mat>();
        private int _tileSize;
        private object _lockObject = new object();
        private bool _disposed = false;

        public TileMap(int tileSize = 256)
        {
            _tileSize = tileSize;
            // Инициализируем начальный тайл
            EnsureTileExists(0, 0);
        }

        private string GetTileKey(int tileX, int tileY)
        {
            return $"{tileX}_{tileY}";
        }

        private (int tileX, int tileY) GetTileIndices(double worldX, double worldY)
        {
            int tileX = (int)Math.Floor(worldX / _tileSize);
            int tileY = (int)Math.Floor(worldY / _tileSize);
            return (tileX, tileY);
        }

        private (int localX, int localY) GetLocalCoordinates(double worldX, double worldY)
        {
            int localX = (int)(worldX % _tileSize);
            int localY = (int)(worldY % _tileSize);
            if (localX < 0) localX += _tileSize;
            if (localY < 0) localY += _tileSize;
            return (localX, localY);
        }

        private void EnsureTileExists(int tileX, int tileY)
        {
            string key = GetTileKey(tileX, tileY);
            lock (_lockObject)
            {
                if (!_tiles.ContainsKey(key) && !_disposed)
                {
                    _tiles[key] = new Mat(_tileSize, _tileSize, MatType.CV_8UC3, new Scalar(255, 255, 255));
                }
            }
        }

        public void AddPoint(double worldX, double worldY, Scalar color, int radius = 3)
        {
            if (_disposed) return;

            var (tileX, tileY) = GetTileIndices(worldX, worldY);
            var (localX, localY) = GetLocalCoordinates(worldX, worldY);

            EnsureTileExists(tileX, tileY);

            string key = GetTileKey(tileX, tileY);
            lock (_lockObject)
            {
                if (!_disposed && _tiles.ContainsKey(key))
                {
                    if (localX >= 0 && localX < _tileSize && localY >= 0 && localY < _tileSize)
                    {
                        Cv2.Circle(_tiles[key], new OpenCvSharp.Point(localX, localY), radius, color, -1);
                    }
                }
            }
        }

        public void AddLine(double startX, double startY, double endX, double endY, Scalar color, int thickness = 2)
        {
            if (_disposed) return;

            var (startTileX, startTileY) = GetTileIndices(startX, startY);
            var (endTileX, endTileY) = GetTileIndices(endX, endY);

            // Для простоты рисуем линию в начальном тайле
            var (localStartX, localStartY) = GetLocalCoordinates(startX, startY);
            var (localEndX, localEndY) = GetLocalCoordinates(endX, endY);

            EnsureTileExists(startTileX, startTileY);
            string key = GetTileKey(startTileX, startTileY);

            lock (_lockObject)
            {
                if (!_disposed && _tiles.ContainsKey(key))
                {
                    Cv2.Line(_tiles[key],
                        new OpenCvSharp.Point(localStartX, localStartY),
                        new OpenCvSharp.Point(localEndX, localEndY),
                        color, thickness);
                }
            }
        }

        public void SaveTiles(string directoryPath)
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                if (_disposed) return;

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                foreach (var kvp in _tiles)
                {
                    try
                    {
                        string filePath = Path.Combine(directoryPath, $"tile_{kvp.Key}.png");
                        Cv2.ImWrite(filePath, kvp.Value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка сохранения тайла {kvp.Key}: {ex.Message}");
                    }
                }
            }
        }

        public Mat GetFullMap()
        {
            if (_disposed || _tiles.Count == 0)
                return new Mat(256, 256, MatType.CV_8UC3, new Scalar(255, 255, 255));

            lock (_lockObject)
            {
                if (_disposed) return new Mat();

                try
                {
                    // Находим границы всех тайлов
                    int minX = int.MaxValue, minY = int.MaxValue;
                    int maxX = int.MinValue, maxY = int.MinValue;

                    foreach (var kvp in _tiles.Keys)
                    {
                        var parts = kvp.Split('_');
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

                    int width = (maxX - minX + 1) * _tileSize;
                    int height = (maxY - minY + 1) * _tileSize;

                    if (width <= 0 || height <= 0)
                        return new Mat(256, 256, MatType.CV_8UC3, new Scalar(255, 255, 255));

                    Mat fullMap = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));

                    foreach (var kvp in _tiles)
                    {
                        var parts = kvp.Key.Split('_');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int tileX) &&
                            int.TryParse(parts[1], out int tileY))
                        {
                            int x = (tileX - minX) * _tileSize;
                            int y = (tileY - minY) * _tileSize;

                            if (x >= 0 && y >= 0 && x + _tileSize <= width && y + _tileSize <= height)
                            {
                                var roi = new Rect(x, y, _tileSize, _tileSize);
                                var tileRoi = new Mat(fullMap, roi);
                                kvp.Value.CopyTo(tileRoi);
                            }
                        }
                    }

                    return fullMap;
                }
                catch
                {
                    return new Mat(256, 256, MatType.CV_8UC3, new Scalar(255, 255, 255));
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                lock (_lockObject)
                {
                    foreach (var tile in _tiles.Values)
                    {
                        tile?.Dispose();
                    }
                    _tiles.Clear();
                }
            }
        }
    }
}