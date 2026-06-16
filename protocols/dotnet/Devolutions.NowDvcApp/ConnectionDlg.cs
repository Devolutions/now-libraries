using System.Diagnostics;
using System.IO.Pipes;

using Devolutions.NowClient;

namespace Devolutions.NowDvcApp
{
    public partial class ConnectionDlg : Form
    {
        public ConnectionDlg()
        {
            InitializeComponent();
        }

        private void ConnectionDlg_Load(object sender, EventArgs e)
        {
            // Set channel id from command line argument.
            var channelId = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
            if (channelId is not null)
            {
                _channelId = channelId;
            }

            Text = $"NowProto DVC ({_channelId})";

            if (!CaptureInstanceLock())
            {
                // Mutex already exists, another instance is running
                MessageBox.Show(
                    "Another instance of the application is already running.",
                    "Instance Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                Application.Exit();
                return;
            }

            // Give application 10 seconds to connect to DVC
            _ = Task.Run(async () =>
            {
                await Task.Delay(10000);

                if (!_connected)
                {
                    _watchdogTriggered = true;
                    Invoke(new Action(Application.Exit));
                    return;
                }


            });

            // Run connection sequence
            _ = Task.Run(async () =>
            {
                var pipeName = $"now-proto-{_channelId}";

                var transport = await NowClientPipeTransport.Connect(pipeName);

                Trace.WriteLine("Connected to DVC pipe");

                // Connect and negotiate capabilities.
                var client = await NowClient.NowClient.Connect(transport);

                Debug.WriteLine("NOW channel has been connected!");

                _connected = true;


                Invoke(() =>
                {
                    Trace.WriteLine("String DVC dialog");
                    var dvcDialog = new DvcDialog(_channelId, client);
                    Hide();
                    dvcDialog.Show();
                });

                // Quick and dirty solution to detect when the channel is dead.
                // NowClient API should be updated to provide a better way to
                // detect this.
                while (!client.IsTerminated)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                Debug.WriteLine("NOW channel has been terminated!");

                Application.Exit();
            });
        }

        private void ConnectionDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_connected && _watchdogTriggered)
            {
                MessageBox.Show(
                    "DVC connection failed",
                    "Connection Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private bool CaptureInstanceLock()
        {
            var instanceLockName = $"Global\\now-proto-client-{_channelId}";

            var instanceLock = new Mutex(
                false,
                instanceLockName,
                out bool createdNew
            );

            if (createdNew)
            {
                GC.KeepAlive(instanceLock);
            }

            return createdNew;
        }

        private bool _connected = false;
        private bool _watchdogTriggered = false;

        // Channel ID is required to distinguish between different DVC connections.
        // The default "GLOBAL" id is used for debugging.
        private string _channelId = "GLOBAL";
    }
}