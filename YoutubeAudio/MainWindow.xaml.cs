using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.Emit;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using NAudio.Gui;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace YoutubeAudio
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        private int currentUnicode = 0xE052;

        private DispatcherTimer playerTimer;
        private DispatcherTimer loadingTimer;
        private WaveOutEvent outputDevice;
        private MediaFoundationReader audioReader;

        private bool playButtonClicked = false;
        private bool isSliderMouseDown = false;
        private bool abcd = false;
        private bool efgh = false;

        public MainWindow()
        {
            InitializeComponent();

            outputDevice = new WaveOutEvent();

            loadingTimer = new DispatcherTimer();
            loadingTimer.Interval = TimeSpan.FromMilliseconds(25);
            loadingTimer.Tick += (object sender, EventArgs e) =>
            {
                if (currentUnicode > 0xE0CB)
                {
                    currentUnicode = 0xE052;
                }
                int unicodeValue = int.Parse($"{currentUnicode:X}", NumberStyles.HexNumber);
                string unicodeCharacter = char.ConvertFromUtf32(unicodeValue);
                LoadingCircle.Content = unicodeCharacter;
                currentUnicode++;
            };

            playerTimer = new DispatcherTimer();
            playerTimer.Interval = TimeSpan.FromSeconds(1);
            playerTimer.Tick += (s, args) =>
            {
                if (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    if (!isSliderMouseDown)
                    {
                        abcd = true;
                        TimeSlider.Value = audioReader.CurrentTime.TotalSeconds;
                        abcd = false;
                    }
                    TimeLabel.Content = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)audioReader.CurrentTime.TotalHours, audioReader.CurrentTime.Minutes, audioReader.CurrentTime.Seconds);
                }
                if (outputDevice.PlaybackState == PlaybackState.Stopped)
                {
                    playerTimer.Stop();
                }
            };

            outputDevice.PlaybackStopped += PlaybackStopped;
        }

        private void playAudioFromUrl(string url)
        {
            audioReader = new MediaFoundationReader(url);
            outputDevice.Init(audioReader);
            outputDevice.Play();
            Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate
            {
                hideOverlayContent(hideOverlayAfter: 0.1, action: () =>
                {
                    SliderParent.Opacity = 1;
                    SliderParent.IsEnabled = true;
                    TimeSlider.Maximum = audioReader.TotalTime.TotalSeconds;

                    PlayButton.Content = char.ConvertFromUtf32(int.Parse($"{0xE769:X}", NumberStyles.HexNumber));
                    playerTimer.Start();
                });
            }));
        }

        async private Task playAudio()
        {
            string videoUrl = "";
            Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate
            {
                showOverlay();
                videoUrl = UrlTextBox.Text;
            }));
            Regex regex = new Regex(@"(?:https?:\/\/)?(?:www\.)?(?:youtube\.com\/(?:[^\/\n\s]+\/\S+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([a-zA-Z0-9_-]{11})");
            Match match = regex.Match(videoUrl);
            if (!match.Success)
            {
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate
                {
                    showOverlayContent("Error: Failed to parse url.", icon: char.ConvertFromUtf32(int.Parse($"{0xE8C9:X}", NumberStyles.HexNumber)), hideContentAfter: 2, hideOverlayAfter: 0.1);
                }));
                return;
            }
            string videoId = match.Groups[1].Value;
            Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate
            {
                showOverlayContent("Fetching...", useLoading: true);
            }));
            HttpClient client = new HttpClient();
            object jsonBody = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "IOS",
                        clientVersion = "19.09.3",
                        deviceModel = "iPhone14,3",
                        userAgent = "com.google.ios.youtube/19.09.3 (iPhone14,3; U; CPU iOS 15_6 like Mac OS X)",
                        hl = "en",
                        timeZone = "UTC",
                        utcOffsetMinutes = 0
                    }
                },
                videoId,
                playbackContext = new
                {
                    contentPlaybackContext = new
                    {
                        html5Preference = "HTML5_PREF_WANTS"
                    }
                },
                contentCheckOk = true,
                racyCheckOk = true
            };

            client.DefaultRequestHeaders.Add("X-YouTube-Client-Name", "5");
            client.DefaultRequestHeaders.Add("X-YouTube-Client-Version", "19.09.3");
            client.DefaultRequestHeaders.Add("Origin", "https://www.youtube.com");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("com.google.ios.youtube/19.09.3 (iPhone14,3; U; CPU iOS 15_6 like Mac OS X)");

            HttpResponseMessage response = await client.PostAsync($"https://www.youtube.com/youtubei/v1/player?key=AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc&prettyPrint=false", new StringContent(JsonConvert.SerializeObject(jsonBody), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate
                {
                    hideOverlayContent(action: () =>
                    {
                        showOverlayContent($"Error: Failed to get response. ({response.StatusCode})", icon: char.ConvertFromUtf32(int.Parse($"{0xE8C9:X}", NumberStyles.HexNumber)), hideContentAfter: 2, hideOverlayAfter: 0.1);
                    });
                }));
                return;
            }
            string result = await response.Content.ReadAsStringAsync();
            JObject data = JObject.Parse(result);
            string status = (string)data["playabilityStatus"]["status"];
            if (status == "ERROR")
            {
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate
                {
                    hideOverlayContent(action: () =>
                    {
                        showOverlayContent($"Error: {data["playabilityStatus"]["reason"]}", icon: char.ConvertFromUtf32(int.Parse($"{0xE8C9:X}", NumberStyles.HexNumber)), hideContentAfter: 2, hideOverlayAfter: 0.1);
                    });
                }));
                return;
            }
            JArray formats = (JArray) data["streamingData"]["adaptiveFormats"];
            Dictionary<int, string> urls = new Dictionary<int, string>();
            int count = 0;
            foreach (JObject format in formats)
            {
                if (((string) format["mimeType"]).StartsWith("audio"))
                {
                    urls[(int) format["averageBitrate"]] = (string) format["url"];
                    count++;
                }
            }

            if (count > 0)
            {
                string maxBitrateUrl = urls[urls.Keys.Max()];
                playAudioFromUrl(maxBitrateUrl);
                return;
            }
            else
            {
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate
                {
                    hideOverlayContent(action: () =>
                    {
                        showOverlayContent($"Error: Failed to get audio url.", icon: char.ConvertFromUtf32(int.Parse($"{0xE8C9:X}", NumberStyles.HexNumber)), hideContentAfter: 2, hideOverlayAfter: 0.1);
                    });
                }));
                return;
            }
        }

        private void showOverlayContent(string message ,string icon = "", bool useLoading = false, double hideContentAfter = 0, double hideOverlayAfter = 0)
        {
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            LoadingOverlayContent.Opacity = 0;
            LoadingInfo.Opacity = 1;
            LoadingInfo.Content = message;
            fadeIn.Completed += (s, e) =>
            {
                if (hideOverlayAfter > 0 && hideContentAfter > 0)
                {
                    DispatcherTimer waitTime = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(hideContentAfter)
                    };
                    waitTime.Tick += (s2, e2) =>
                    {
                        hideOverlayContent(hideOverlayAfter);
                        waitTime.Stop();
                    };
                    waitTime.Start();
                }
            };
            if (useLoading)
            {
                LoadingIcon.Opacity = 0;
                LoadingCircle.Opacity = 1;
                currentUnicode = 0xE052;
                loadingTimer.Start();
            }
            else
            {
                if (loadingTimer.IsEnabled) loadingTimer.Stop();
                LoadingIcon.Content = icon;
                LoadingIcon.Opacity = 1;
                LoadingCircle.Opacity = 0;
            }
            LoadingOverlayContent.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            return;
        }
        private void hideOverlayContent(double hideOverlayAfter = 0, Action action = null)
        {
            if(loadingTimer.IsEnabled) loadingTimer.Stop();
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            fadeOut.Completed += (s, e) =>
            {
                LoadingOverlayContent.Opacity = 0;
                if (hideOverlayAfter > 0)
                {
                    DispatcherTimer waitTime = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(hideOverlayAfter)
                    };
                    waitTime.Tick += (s2, e2) =>
                    {
                        hideOverlay();
                        waitTime.Stop();
                    };
                    waitTime.Start();
                }
                if(action != null)
                {
                    action();
                }
            };
            LoadingOverlayContent.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void showOverlay()
        {
            LoadingOverlayContent.Opacity = 0;
            LoadingOverlay.Opacity = 0;
            LoadingOverlay.Visibility = Visibility.Visible;
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        private void hideOverlay()
        {
            if (loadingTimer.IsEnabled) loadingTimer.Stop();
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            fadeOut.Completed += (s, e) =>
            {
                LoadingOverlay.Opacity = 0;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            };
            LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        // Events
        private void PlaybackStopped(object s, object e)
        {
            //PlayButton.Content = char.ConvertFromUtf32(int.Parse($"{0xE768:X}", NumberStyles.HexNumber));
            TimeSlider.Value = 0;
            TimeLabel.Content = "00:00:00";
            audioReader.CurrentTime = TimeSpan.FromSeconds(0);
            outputDevice.Play();
            if (playerTimer.IsEnabled) playerTimer.Stop();
            playerTimer.Start();
        }

        private void DragMove(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void ExitButton_onClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void MinimizeButton_onClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void PlayButton_onClick(object sender, RoutedEventArgs e)
        {
            if(!playButtonClicked)
            {
                playButtonClicked = true;
                switch (outputDevice.PlaybackState)
                {
                    case PlaybackState.Playing:
                        PlayButton.Content = char.ConvertFromUtf32(int.Parse($"{0xE768:X}", NumberStyles.HexNumber));
                        outputDevice.Pause();
                        break;
                    case PlaybackState.Paused:
                        PlayButton.Content = char.ConvertFromUtf32(int.Parse($"{0xE769:X}", NumberStyles.HexNumber));
                        outputDevice.Play();
                        break;
                    case PlaybackState.Stopped:
                        Thread audioThread = new Thread(async () => await playAudio());
                        audioThread.Start();
                        break;
                    default:
                        break;
                }
                playButtonClicked = false;
            }
        }

        private void SliderParent_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            isSliderMouseDown = true;
            efgh = false;
        }

        private void SliderParent_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (audioReader != null && outputDevice != null && outputDevice.PlaybackState != PlaybackState.Stopped && efgh)
            {
                audioReader.CurrentTime = TimeSpan.FromSeconds(TimeSlider.Value);
                isSliderMouseDown = false;
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            showOverlay();
            outputDevice.PlaybackStopped -= PlaybackStopped;
            outputDevice.Stop();
            if (playerTimer.IsEnabled) playerTimer.Stop();
            outputDevice = new WaveOutEvent();
            audioReader = null;
            PlayButton.Content = char.ConvertFromUtf32(int.Parse($"{0xE768:X}", NumberStyles.HexNumber));
            TimeLabel.Content = "00:00:00";
            SliderParent.IsEnabled = false;
            SliderParent.Opacity = 0.5;
            TimeSlider.Value = 0;
            TimeSlider.Maximum = 10;
            isSliderMouseDown = false;
            UrlTextBox.Text = "";
            outputDevice.PlaybackStopped += PlaybackStopped;
            hideOverlay();
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!abcd)
            {
                efgh = true;
            }
        }
    }
}
