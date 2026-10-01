using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lab4_Task1;

public partial class MainWindow : Window
{
    private readonly StackPanel[] _bars;
    private readonly ScrollViewer[] _scrollers;
    private readonly TextBlock[] _states;
    private readonly Border[] _stateIndicators;
    private readonly TextBlock[] _stats;
    private RunSession? _session;
    private int _generation;

    public MainWindow()
    {
        InitializeComponent();
        _bars = [Bar1, Bar2, Bar3];
        _scrollers = [Scroll1, Scroll2, Scroll3];
        _states = [State1, State2, State3];
        _stateIndicators = [StateBorder1, StateBorder2, StateBorder3];
        _stats = [Stats1, Stats2, Stats3];
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out int initialMs) || initialMs < 100 || initialMs > 5000)
        {
            MessageBox.Show("Введіть початковий час від 100 до 5000 мс.", "Перевірка даних",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ClearDisplay();
        var session = new RunSession(++_generation, initialMs, RandomCheck.IsChecked == true);
        _session = session;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        IntervalBox.IsEnabled = false;
        RandomCheck.IsEnabled = false;
        SummaryText.Text = session.UseRandomTransfers
            ? $"Кожен потік почне з {initialMs} мс. Після першого кола передаватимуться випадкові інтервали."
            : $"Кожен потік почне з {initialMs} мс. Хід передається по колу: 1 → 2 → 3 → 1.";

        session.Inboxes[0].Add(initialMs);
        for (int i = 0; i < 3; i++)
        {
            int workerIndex = i;
            session.Threads[i] = new Thread(() => RunWorker(session, workerIndex))
            {
                IsBackground = true,
                Name = $"Потік {i + 1}"
            };
            session.Threads[i].Start();
        }
        _ = ObserveRunAsync(session);
    }

    private async Task ObserveRunAsync(RunSession session)
    {
        await Task.Run(() =>
        {
            session.Ready.Wait();
            session.StartGate.Set();
            foreach (Thread thread in session.Threads)
                thread.Join();
        });

        await Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                bool showSummary = session.Generation == _generation && !session.Cleared;
                if (showSummary)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        _states[i].Text = "Зупинено";
                        _stateIndicators[i].Background = (Brush)new BrushConverter().ConvertFromString("#344563")!;
                        _states[i].Foreground = Brushes.White;
                        _stats[i].Text = $"Робота: {session.WorkMs[i]} мс · Очікування: {session.IdleMs[i]} мс";
                    }
                    SummaryText.Text = MakeSummary(session);
                }

                _session = null;
                SetIdleControls();
            }
        }, DispatcherPriority.ContextIdle);
        session.Dispose();
    }

    private void RunWorker(RunSession session, int index)
    {
        int nextIndex = (index + 1) % 3;
        try
        {
            session.Ready.Signal();
            session.StartGate.Wait(session.Cancellation.Token);
            PostState(session, index, "Очікує", "#F02020");

            while (!session.Cancellation.IsCancellationRequested)
            {
                int durationMs;
                try
                {
                    durationMs = session.Inboxes[index].Take(session.Cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                PostState(session, index, "Працює", "#2020F0");

                if (session.Cancellation.Token.WaitHandle.WaitOne(durationMs))
                    break;

                session.HasWorked[index] = true;
                RecordTurn(session, index, durationMs);
                PostState(session, index, "Очікує", "#F02020");

                int transferredMs;
                if (!session.HasWorked[nextIndex])
                    transferredMs = session.InitialMs; 
                else if (session.UseRandomTransfers)
                    transferredMs = Random.Shared.Next(250, 1201);
                else
                    transferredMs = durationMs;

                session.Inboxes[nextIndex].Add(transferredMs, session.Cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            PostState(session, index, "Зупинено", "#344563");
        }
    }

    private void RecordTurn(RunSession session, int activeIndex, int durationMs)
    {
        session.WorkMs[activeIndex] += durationMs;
        for (int i = 0; i < 3; i++)
        {
            if (i != activeIndex)
                session.IdleMs[i] += durationMs;
        }

        PostTurn(session, activeIndex, durationMs);
    }

    private void PostState(RunSession session, int index, string state, string color)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCurrent(session)) return;
                _states[index].Text = state;
                _stateIndicators[index].Background = (Brush)new BrushConverter().ConvertFromString(color)!;
                _states[index].Foreground = state == "Готовий" ? Brushes.SlateGray : Brushes.White;
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void PostTurn(RunSession session, int activeIndex, int durationMs)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCurrent(session)) return;

                for (int i = 0; i < 3; i++)
                {
                    bool isWork = i == activeIndex;
                    var block = new Border
                    {
                        Height = 25,
                        Background = (Brush)new BrushConverter().ConvertFromString(isWork ? "#2020F0" : "#F02020")!,
                        BorderBrush = Brushes.White,
                        BorderThickness = new Thickness(0, 0, 0, 2),
                        Child = new TextBlock
                        {
                            Text = $"{(isWork ? "Робота" : "Очікування")}: {durationMs} мс",
                            Foreground = Brushes.White,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    };
                    _bars[i].Children.Insert(0, block);
                    _scrollers[i].ScrollToTop();
                    _stats[i].Text = $"Робота: {session.WorkMs[i]} мс · Очікування: {session.IdleMs[i]} мс";
                }
            }));
        }
        catch (InvalidOperationException) { }
    }

    private bool IsCurrent(RunSession session) =>
        ReferenceEquals(_session, session) && session.Generation == _generation;

    private static string MakeSummary(RunSession session) =>
        "Зупинено. " + string.Join("   ", Enumerable.Range(0, 3)
            .Select(i => $"Потік {i + 1}: робота {session.WorkMs[i]} мс, очікування {session.IdleMs[i]} мс."));

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.RequestStop();
        StopButton.IsEnabled = false;
        SummaryText.Text = "Зупиняю потоки…";
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
        {
            _session.Cleared = true;
            _generation++;
            _session.RequestStop();
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
        }
        else SetIdleControls();

        ClearDisplay();
        SummaryText.Text = "Смуги очищено. Введи початковий час і натисни «Старт».";
    }

    private void ClearDisplay()
    {
        foreach (StackPanel bar in _bars) bar.Children.Clear();
        foreach (ScrollViewer scroller in _scrollers) scroller.ScrollToTop();
        for (int i = 0; i < 3; i++)
        {
            _states[i].Text = "Готовий";
            _stateIndicators[i].Background = (Brush)new BrushConverter().ConvertFromString("#E8EDF5")!;
            _states[i].Foreground = Brushes.SlateGray;
            _stats[i].Text = "Робота: 0 мс · Очікування: 0 мс";
        }
    }

    private void SetIdleControls()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        IntervalBox.IsEnabled = true;
        RandomCheck.IsEnabled = true;
    }

    private sealed class RunSession : IDisposable
    {
        public RunSession(int generation, int initialMs, bool useRandomTransfers)
        {
            Generation = generation;
            InitialMs = initialMs;
            UseRandomTransfers = useRandomTransfers;
            Cancellation = new CancellationTokenSource();
            StartGate = new ManualResetEventSlim(false);
            Ready = new CountdownEvent(3);
            Inboxes = Enumerable.Range(0, 3).Select(_ => new BlockingCollection<int>()).ToArray();
            Threads = new Thread[3];
            WorkMs = new long[3];
            IdleMs = new long[3];
            HasWorked = new bool[3];
        }

        public int Generation { get; }
        public int InitialMs { get; }
        public bool UseRandomTransfers { get; }
        public bool Cleared { get; set; }
        public CancellationTokenSource Cancellation { get; }
        public ManualResetEventSlim StartGate { get; }
        public CountdownEvent Ready { get; }
        public BlockingCollection<int>[] Inboxes { get; }
        public Thread[] Threads { get; }
        public long[] WorkMs { get; }
        public long[] IdleMs { get; }
        public bool[] HasWorked { get; }

        public void RequestStop()
        {
            if (!Cancellation.IsCancellationRequested)
                Cancellation.Cancel();
            StartGate.Set();
        }

        public void Dispose()
        {
            foreach (BlockingCollection<int> inbox in Inboxes) inbox.Dispose();
            StartGate.Dispose();
            Ready.Dispose();
            Cancellation.Dispose();
        }
    }
}
