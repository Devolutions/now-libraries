using System.Text;

using Devolutions.NowClient;
using Devolutions.NowProto.Exceptions;

namespace Devolutions.NowDvcApp
{
    public partial class ExecSessionDlg : Form
    {
        public ExecSessionDlg()
        {
            InitializeComponent();
        }

        private void SetGuiStateFinished()
        {
            stdinInput.Enabled = false;
            stdinInput.ReadOnly = true;
            crCheckBox.Enabled = false;
            lfCheckBox.Enabled = false;
            eofCheckBox.Enabled = false;
            sendStdinButton.Enabled = false;
            cancelButton.Enabled = false;
            abortButton.Enabled = false;
        }

        public async Task ProcessExecSession(ExecSession session, string kind)
        {
            sessionKindText.Text = kind;
            _execSession = session;

            try
            {
                var code = await _execSession.GetResult();
                logInput.Text += $"Finished with exit code {code}\n";
                sessionStatusText.Text = "finished";
            }
            catch (NowStatusException exception)
            {
                logInput.Text += $"Server error: {exception}";
                sessionStatusText.Text = "failed";
            }
            catch (NowClientException clientException)
            {
                logInput.Text += $"Client error: {clientException}";
                sessionStatusText.Text = "failed";
            }
            catch (Exception sessionException)
            {
                logInput.Text += $"Unexpected error: {sessionException}";
                sessionStatusText.Text = "failed";
            }

            SetGuiStateFinished();
        }

        private void AdjustControlsStarted()
        {
            stdinInput.Enabled = true;
            sessionStatusText.Text = "running";
            stdinInput.Enabled = true;
            sendStdinButton.Enabled = true;
            cancelButton.Enabled = true;
        }

        public void OnSessionStarted()
        {
            if (InvokeRequired)
            {
                Invoke(AdjustControlsStarted);
                return;
            }

            AdjustControlsStarted();
        }

        public void OnSessionStdout(ArraySegment<byte> outData, bool last)
        {
            if (stdoutInput.InvokeRequired)
            {
                stdoutInput.Invoke(() => OnSessionDataIn(stdoutInput, outData, last));
            }
            else
            {
                OnSessionDataIn(stdoutInput, outData, last);
            }
        }

        public void OnSessionStderr(ArraySegment<byte> outData, bool last)
        {
            if (stderrInput.InvokeRequired)
            {
                stderrInput.Invoke(() => OnSessionDataIn(stderrInput, outData, last));
            }
            else
            {
                OnSessionDataIn(stderrInput, outData, last);
            }
        }

        private static void OnSessionDataIn(TextBox target, ArraySegment<byte> outData, bool last)
        {
            var textToAppend = string.Empty;

            try
            {
                if (outData.Count != 0)
                {
                    textToAppend = Encoding.UTF8.GetString(outData.ToArray());
                }

            }
            catch (Exception)
            {
                textToAppend = $"<RAW BYTES ({outData.Count})>";
            }

            if (last)
            {
                textToAppend += "<EOF>";
            }

            target.AppendText(textToAppend);
        }

        private async void SendStdinButtonClick(object sender, EventArgs e)
        {
            if (_execSession == null)
            {
                return;
            }

            var str = stdinInput.Text;

            if (crCheckBox.Checked)
            {
                str += "\r";
            }

            if (lfCheckBox.Checked)
            {
                str += "\n";
            }

            var data = Encoding.UTF8.GetBytes(str);

            await _execSession.SendStdin(data, eofCheckBox.Checked);

            if (eofCheckBox.Checked)
            {
                sendStdinButton.Enabled = false;
                stdinInput.Enabled = false;
                stdinInput.ReadOnly = true;
                crCheckBox.Enabled = false;
                lfCheckBox.Enabled = false;
                eofCheckBox.Enabled = false;
            }

            logInput.Text += $"Sent stdin data bytes (data) \n";
        }

        private async void AbortButtonClick(object sender, EventArgs e)
        {
            if (_execSession == null)
            {
                return;
            }

            var abortCode = uint.Parse(abortCodeInput.Text);
            await _execSession.Abort(abortCode);

            logInput.Text += $"Aborted with code {abortCode}\n";
            sessionStatusText.Text = "aborted";
            SetGuiStateFinished();
        }

        private async void CancelButtonClick(object sender, EventArgs e)
        {
            if (_execSession == null)
            {
                return;
            }

            logInput.Text += "Sending cancel request...\n";
            try
            {
                await _execSession.Cancel();
            }
            catch (NowStatusException exception)
            {
                logInput.Text += $"Cancel failed: {exception}\n";
            }

            logInput.Text += "Session cancelled\n";
            sessionStatusText.Text = "cancelled";
        }

        private ExecSession? _execSession = null;
    }
}