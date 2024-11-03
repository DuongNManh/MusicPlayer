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
        }

        private double[] CreateFrequencyBands(int bandCount)
        {
            var bands = new double[bandCount + 1];
            double minFreq = 20;
            double maxFreq = 20000;

            for (int i = 0; i <= bandCount; i++)
            {
                double percent = (double)i / bandCount;
                bands[i] = minFreq * Math.Pow(maxFreq / minFreq, Math.Pow(percent, 0.5));
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
                    new GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 85, 0), 0),
                    new GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 85, 1), 0.3),
                    new GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 85, 2), 1)
                }
            };
        }

        public void UpdateSpectrum(float[] audioData)
        {
            if (audioData == null || audioData.Length < _fftLength) return;

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
                            if (freq < 40)         // Sub-bass (20-40 Hz)
                                weight = 2.0;
                            else if (freq < 80)    // Bass (40-80 Hz)
                                weight = 2.0;
                            else if (freq < 160)   // Bass (80-160 Hz)
                                weight = 2.0;
                            else if (freq < 300)   // Low-mids (160-300 Hz)
                                weight = 2.0;
                            else if (freq < 500)   // Mids (300-500 Hz)
                                weight = 2.0;
                            else if (freq < 2000)  // Upper-mids (500-2000 Hz)
                                weight = 2.5;
                            else if (freq < 4000)  // Presence (2-4 kHz)
                                weight = 3.0;
                            else if (freq < 8000)  // Brilliance (4-8 kHz)
                                weight = 3.5;
                            else                   // Air (8-20 kHz)
                                weight = 4.0;

                            double dbWeight = GetDBWeight(freq);
                            weight *= dbWeight;

                            double freqScale = Math.Log10(freq + 1) / Math.Log10(20000);
                            weight *= (freqScale);

                            sum += magnitude * weight;
                            binCount++;
                        }

                        double average = binCount > 0 ? sum / binCount : 0;

                        double positionScale = 1.0;
                        double normalizedPosition = (double)i / _visualizationBars.Length;

                        if (normalizedPosition < 0.1)       // Sub-bass (0-10%)
                            positionScale = 4.0;
                        else if (normalizedPosition < 0.2)  // Bass (10-20%)
                            positionScale = 3.5;
                        else if (normalizedPosition < 0.3)  // Low-mids (20-30%)
                            positionScale = 3.0;
                        else if (normalizedPosition < 0.5)  // Mids (30-50%)
                            positionScale = 2.5;
                        else if (normalizedPosition < 0.7)  // Upper-mids (50-70%)
                            positionScale = 3.0;
                        else if (normalizedPosition < 0.85) // Presence (70-85%)
                            positionScale = 3.5;
                        else                               // Air (85-100%)
                            positionScale = 4.0;

                        double value = Math.Log10(1 + average * 20) * positionScale;
                        heights[i] = Math.Min(_maxHeight, value * _maxHeight * 8);
                    }

                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        for (int i = 0; i < _visualizationBars.Length; i++)
                        {
                            var bar = _visualizationBars[i];
                            double targetHeight = heights[i];
                            double currentHeight = bar.Height;

                            double smoothingUp = 0.5;    // Faster rise
                            double smoothingDown = 0.1;  // Slower fall
                            double smoothing = targetHeight > currentHeight ? smoothingUp : smoothingDown;

                            bar.Height = Math.Max(2, (currentHeight * (1 - smoothing)) + (targetHeight * smoothing));
                        }
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Visualization error: {ex.Message}");
                }
            });
        }

        private double GetDBWeight(double freq)
        {
            if (freq < 20) return 0.1;
            else if (freq < 50) return 0.5;
            else if (freq < 100) return 0.8;
            else if (freq < 200) return 1.0;
            else if (freq < 500) return 1.1;
            else if (freq < 1000) return 1.2;
            else if (freq < 2000) return 1.3;
            else if (freq < 4000) return 1.4;
            else if (freq < 8000) return 1.3;
            else if (freq < 16000) return 1.2;
            else return 1.0;
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
    }
}