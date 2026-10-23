using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SnapBar.App;

// Explicit --fixture --delayed-uia mode only. Emulates a provider that becomes
// available asynchronously after the first native accessibility query.
internal sealed class DelayedTextBox : TextBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new DelayedTextBoxPeer(this);

    private sealed class DelayedTextBoxPeer(DelayedTextBox owner) : TextBoxAutomationPeer(owner)
    {
        private bool requested, ready;
        public override object? GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Text && !ready)
            {
                if (!requested)
                {
                    requested = true;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    timer.Tick += (_, _) => { ready = true; timer.Stop(); };
                    timer.Start();
                }
                return null;
            }
            return base.GetPattern(patternInterface);
        }
    }
}
