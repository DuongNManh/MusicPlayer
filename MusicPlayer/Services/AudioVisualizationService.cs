using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace MusicPlayer.Services
{
    public class AudioVisualizationService
    {
        private readonly int _fftLength;
        private readonly int _maxHeight;
        private readonly System.Windows.Shapes.Rectangle[] _visualizationBars;
        private readonly Canvas _canvas;
        private readonly Complex[] _fftBuffer;
        private readonly float[] _window;
        private readonly double[] _frequencyBands;
        private readonly double _sampleRate = 44100;
        private readonly object _lockObject = new object();
        private readonly int _updateInterval = 33; // ~30 FPS instead of unlimited
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _isActive = true;
        private const int MINIMUM_UPDATE_THRESHOLD = 2; // Minimum height change to trigger update

        public AudioVisualizationService(Canvas visualizationCanvas, int barsCount = 128, int maxHeight = 150)
        {
            _canvas = visualizationCanvas;
            _fftLength = 8192;
            _maxHeight = maxHeight;
            _visualizationBars = new System.Windows.Shapes.Rectangle[barsCount];
            _fftBuffer = new Complex[_fftLength];
            _window = CreateWindow(_fftLength);
            _frequencyBands = CreateFrequencyBands(barsCount);

            InitializeVisualBars();

            visualizationCanvas.SizeChanged += (s, e) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateBarsLayout());
            visualizationCanvas.IsVisibleChanged += (s, e) =>
            {
                _isActive = visualizationCanvas.IsVisible;
                if (!_isActive)
                {
                    Clear();
                }
            };
        }

        private double[] CreateFrequencyBands(int bandCount)
        {
            var bands = new double[bandCount + 1];

            // Modified frequency ranges:
            // Band 1: 250-500 Hz (Low-mids)
            // Band 2: 500-2000 Hz (Mids)
            // Band 3: 2000-4000 Hz (Upper-mids)
            // Band 4: 4000-8000 Hz (Presence)
            // Band 5: 8000-16000 Hz (Brilliance)
            // Band 6: 16000-20000 Hz (Air)

            int lowMidBars = bandCount / 6;           // 250-500 Hz
            int midBars = bandCount / 6;              // 500-2000 Hz
            int upperMidBars = bandCount / 6;         // 2000-4000 Hz
            int presenceBars = bandCount / 6;         // 4000-8000 Hz
            int brillianceBars = bandCount / 6;       // 8000-16000 Hz
            int airBars = bandCount - lowMidBars - midBars - upperMidBars - presenceBars - brillianceBars;  // 16000-20000 Hz

            for (int i = 0; i <= bandCount; i++)
            {
                if (i <= lowMidBars)
                {
                    // Low-mids (250-500 Hz)
                    double percent = (double)i / lowMidBars;
                    bands[i] = 250.0 * Math.Pow(500.0 / 250.0, percent);
                }
                else if (i <= lowMidBars + midBars)
                {
                    // Mids (500-2000 Hz)
                    double percent = (double)(i - lowMidBars) / midBars;
                    bands[i] = 500.0 * Math.Pow(2000.0 / 500.0, percent);
                }
                else if (i <= lowMidBars + midBars + upperMidBars)
                {
                    // Upper-mids (2000-4000 Hz)
                    double percent = (double)(i - lowMidBars - midBars) / upperMidBars;
                    bands[i] = 2000.0 * Math.Pow(4000.0 / 2000.0, percent);
                }
                else if (i <= lowMidBars + midBars + upperMidBars + presenceBars)
                {
                    // Presence (4000-8000 Hz)
                    double percent = (double)(i - lowMidBars - midBars - upperMidBars) / presenceBars;
                    bands[i] = 4000.0 * Math.Pow(8000.0 / 4000.0, percent);
                }
                else if (i <= lowMidBars + midBars + upperMidBars + presenceBars + brillianceBars)
                {
                    // Brilliance (8000-16000 Hz)
                    double percent = (double)(i - lowMidBars - midBars - upperMidBars - presenceBars) / brillianceBars;
                    bands[i] = 8000.0 * Math.Pow(16000.0 / 8000.0, percent);
                }
                else
                {
                    // Air (16000-20000 Hz)
                    double percent = (double)(i - lowMidBars - midBars - upperMidBars - presenceBars - brillianceBars) / airBars;
                    bands[i] = 16000.0 * Math.Pow(20000.0 / 16000.0, percent);
                }
            }
            return bands;
        }

        private void InitializeVisualBars()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _canvas.Children.Clear();
                double barWidth = (_canvas.ActualWidth / _visualizationBars.Length) - 0.5;

                for (int i = 0; i < _visualizationBars.Length; i++)
                {
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = Math.Max(1.5, barWidth),
                        Fill = CreateBarGradient(),
                        Height = 2,
                        RadiusX = 0.5,
                        RadiusY = 0.5
                    };

                    Canvas.SetLeft(rect, i * (barWidth + 0.5));
                    Canvas.SetBottom(rect, 0);

                    _visualizationBars[i] = rect;
                    _canvas.Children.Add(rect);
                }
            });
        }

        private LinearGradientBrush CreateBarGradient()
        {
            return new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0.5, 0),
                EndPoint = new System.Windows.Point(0.5, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 85, 0), 0)
                }
            };
        }

        public void UpdateSpectrum(float[] audioData)
        {
            if (audioData == null || audioData.Length < _fftLength || !_isActive) return;

            var now = DateTime.Now;
            if ((now - _lastUpdate).TotalMilliseconds < _updateInterval) return;
            _lastUpdate = now;

            Task.Run(() =>
            {
                try
                {
                    var fftBuffer = new Complex[_fftLength];
                    for (int i = 0; i < _fftLength; i++)
                    {
                        fftBuffer[i].X = i < audioData.Length ? audioData[i] * _window[i] : 0;
                        fftBuffer[i].Y = 0;
                    }

                    FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2.0), fftBuffer);

                    var heights = new double[_visualizationBars.Length];
                    double binWidth = _sampleRate / _fftLength;

                    for (int i = 0; i < _visualizationBars.Length; i++)
                    {
                        int startBin = (int)(_frequencyBands[i] / binWidth);
                        int endBin = (int)(_frequencyBands[i + 1] / binWidth);
                        startBin = Math.Max(0, Math.Min(startBin, _fftLength / 2 - 1));
                        endBin = Math.Max(0, Math.Min(endBin, _fftLength / 2));

                        double sum = 0;
                        int binCount = 0;

                        for (int j = startBin; j < endBin; j++)
                        {
                            double magnitude = Math.Sqrt(fftBuffer[j].X * fftBuffer[j].X + fftBuffer[j].Y * fftBuffer[j].Y);
                            double freq = j * binWidth;

                            double weight = 1.0;
                            if (freq < 500)          // Low-mids
                            {
                                weight = 2.0;
                            }
                            else if (freq < 2000)    // Mids
                            {
                                weight = 2.2;
                            }
                            else if (freq < 4000)    // Upper-mids
                            {
                                weight = 2.6;
                            }
                            else if (freq < 8000)    // Presence
                            {
                                weight = 3.4;        // Increased presence boost
                            }
                            else if (freq < 16000)   // Brilliance
                            {
                                weight = 5.8;        // Increased brilliance boost
                            }
                            else                     // Air
                            {
                                weight = 3.0;        // Increased air frequencies boost
                            }

                            // Apply position-based scaling
                            double binPosition = (double)i / _visualizationBars.Length;

                            // Adjust scaling to emphasize higher frequencies
                            if (binPosition < 0.2)        // Low-mids region
                                weight *= 1.0;
                            else if (binPosition < 0.3)   // Mids
                                weight *= 1.2;
                            else if (binPosition < 0.5)   // Upper-mids
                                weight *= 1.4;
                            else if (binPosition < 0.7)   // Presence
                                weight *= 1.6;            // Increased boost
                            else if (binPosition < 0.85)  // Brilliance
                                weight *= 1.8;            // Increased boost
                            else                          // Air
                                weight *= 2.0;            // Increased boost

                            double dbWeight = GetDBWeight(freq);
                            weight *= dbWeight;

                            sum += magnitude * weight;
                            binCount++;
                        }

                        double average = binCount > 0 ? sum / binCount : 0;

                        double positionScale = 1.0;
                        double normalizedPosition = (double)i / _visualizationBars.Length;

                        if (normalizedPosition < 0.1)       // Sub-bass (0-10%)
                            positionScale = 3.0;
                        else if (normalizedPosition < 0.2)  // Bass (10-20%)
                            positionScale = 3.1;
                        else if (normalizedPosition < 0.3)  // Low-mids (20-30%)
                            positionScale = 3.3;
                        else if (normalizedPosition < 0.5)  // Mids (30-50%)
                            positionScale = 2.5;
                        else if (normalizedPosition < 0.7)  // Upper-mids (50-70%)
                            positionScale = 2.5;
                        else if (normalizedPosition < 0.85) // Presence (70-85%)
                            positionScale = 3.5;
                        else                               // Air (85-100%)
                            positionScale = 4.0;

                        double value = Math.Log10(1 + average * 20) * positionScale;
                        heights[i] = Math.Min(_maxHeight, value * _maxHeight * 8);
                    }

                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_isActive) return;

                        for (int i = 0; i < _visualizationBars.Length; i++)
                        {
                            var bar = _visualizationBars[i];
                            double targetHeight = heights[i];
                            double currentHeight = bar.Height;

                            if (Math.Abs(targetHeight - currentHeight) > MINIMUM_UPDATE_THRESHOLD)
                            {
                                double smoothingUp = 0.5;    // Slower rise (was 0.5)
                                double smoothingDown = 0.1; // Slower fall (was 0.1)
                                double smoothing = targetHeight > currentHeight ? smoothingUp : smoothingDown;

                                bar.Height = Math.Max(2, (currentHeight * (1 - smoothing)) + (targetHeight * smoothing));
                            }
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Visualization error: {ex.Message}");
                }
            });
        }

        private double GetDBWeight(double freq)
        {
            if (freq < 500)         // Low-mids
            {
                return 1.0;
            }
            else if (freq < 2000)   // Mids
            {
                return 1.2;
            }
            else if (freq < 4000)   // Upper-mids
            {
                return 1.4;
            }
            else if (freq < 8000)   // Presence
            {
                return 1.6;         // Boosted presence
            }
            else if (freq < 16000)  // Brilliance
            {
                return 1.8;         // Boosted brilliance
            }
            else                    // Air
            {
                return 2.0;         // Boosted air frequencies
            }
        }

        private float[] CreateWindow(int length)
        {
            float[] window = new float[length];
            const double a0 = 0.35875;
            const double a1 = 0.48829;
            const double a2 = 0.14128;
            const double a3 = 0.01168;

            for (int i = 0; i < length; i++)
            {
                double ratio = (double)i / (length - 1);
                window[i] = (float)(a0 - a1 * Math.Cos(2 * Math.PI * ratio)
                                      + a2 * Math.Cos(4 * Math.PI * ratio)
                                      - a3 * Math.Cos(6 * Math.PI * ratio));
            }
            return window;
        }

        private void UpdateBarsLayout()
        {
            if (!_isActive) return;

            double barWidth = (_canvas.ActualWidth / _visualizationBars.Length) - 0.5;
            for (int i = 0; i < _visualizationBars.Length; i++)
            {
                var rect = _visualizationBars[i];
                rect.Width = Math.Max(1.5, barWidth);
                Canvas.SetLeft(rect, i * (barWidth + 0.5));
            }
        }

        public void Clear()
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (_visualizationBars != null)
                            {
                                foreach (var bar in _visualizationBars)
                                {
                                    if (bar != null)
                                    {
                                        bar.Height = 2;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error resetting visualization bars: {ex.Message}");
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during visualization cleanup: {ex.Message}");
                // Safely ignore errors during shutdown
            }
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (!active)
            {
                Clear();
            }
        }

        // Add a Dispose method to properly cleanup resources
        public void Dispose()
        {
            try
            {
                Clear();
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.Invoke(() =>
                    {
                        if (_canvas != null)
                        {
                            _canvas.Children.Clear();
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing visualization service: {ex.Message}");
            }
        }

        // Add this method to reset the visualization state
        public void Reset()
        {
            lock (_lockObject)
            {
                Clear();
                _lastUpdate = DateTime.MinValue;
                // Reset FFT buffer
                for (int i = 0; i < _fftBuffer.Length; i++)
                {
                    _fftBuffer[i].X = 0;
                    _fftBuffer[i].Y = 0;
                }
            }
        }
    }
}