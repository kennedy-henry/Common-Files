using System;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Data;
using System.Data.DataSetExtensions;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Microsoft;
using MethodManager.Core;
using MMScriptObjects;
using MethodManager.Interop;
using MMScriptObjects.ScriptUtils;
using System.IO.Ports;
using System.Threading;
/* 
** The script entry point will be the method Execute() in a unique instance of a class that implements IMMScriptExecutor.
*/
public class MMScriptExecutor : IMMScriptExecutor
{
	// This is the first method that gets executed.
	public void Execute(IMMApp app)
	{
        // Get the COM port and the command from MM4 variables
        string comPort = app.GetVariableValue("COMport");
        string command = app.GetVariableValue("SerialSend");

        // Optional settings (all strings in MM4). Provide sensible defaults if not set.
        string baudStr = app.GetVariableValue("BaudRate");
        int baud = 9600;
        try
        {
            if (!string.IsNullOrEmpty(baudStr)) baud = Convert.ToInt32(baudStr);
        }
        catch { baud = 9600; }

        string timeoutStr = app.GetVariableValue("SerialTimeoutMS");
        int timeoutMs = 3000; // default 3s
        try
        {
            if (!string.IsNullOrEmpty(timeoutStr)) timeoutMs = Convert.ToInt32(timeoutStr);
        }
        catch { timeoutMs = 3000; }

        // If your device terminates lines, set this in MM4 (e.g. "\r\n"). If empty, we just collect whatever arrives.
        string nl = app.GetVariableValue("SerialNewline");
        if (string.IsNullOrEmpty(nl)) nl = "\n";

        // Validate the inputs
        if (string.IsNullOrEmpty(comPort) || string.IsNullOrEmpty(command))
        {
            app.SetVariable("SerialStatus", "COMport or SerialSend is empty");
            return;
        }

        SerialPort port = null;
        try
        {
            port = new SerialPort(comPort, baud, Parity.None, 8, StopBits.One);
            // Configure reasonable defaults; adjust via hardware/needs as required
            port.Handshake = Handshake.None;
            port.NewLine = nl;
            port.ReadTimeout = 200;  // non-blocking style reads in the polling loop
            port.WriteTimeout = 1000;
            port.DtrEnable = false;
            port.RtsEnable = false;

            port.Open();

            // Send the command (line endings handled by device expectations)
            // If your device requires the newline, keep WriteLine; otherwise switch to Write.
            port.WriteLine(command);

            // Poll for response until timeout
            StringBuilder sb = new StringBuilder();
            DateTime start = DateTime.UtcNow;
            bool gotTerminatedLine = false;

            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                try
                {
                    // Read whatever is available
                    int available = port.BytesToRead;
                    if (available > 0)
                    {
                        // Prefer reading what's there without blocking
                        string chunk = port.ReadExisting();
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            sb.Append(chunk);
                            // If a newline-terminated protocol is used, we can stop early when seen
                            if (!string.IsNullOrEmpty(nl) && sb.ToString().IndexOf(nl) >= 0)
                            {
                                gotTerminatedLine = true;
                                break;
                            }
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // Ignore and continue polling
                }

                Thread.Sleep(50); // small delay to avoid busy-wait
            }

            string response = sb.ToString();
            string elapsed = ((int)(DateTime.UtcNow - start).TotalMilliseconds).ToString();

            if (response != null) response = response.Trim();

            if (!string.IsNullOrEmpty(response))
            {
                app.SetVariable("SerialResponse", response);
                if (gotTerminatedLine)
                {
                    app.SetVariable("SerialStatus", "OK: received line-terminated response in " + elapsed + " ms");
                }
                else
                {
                    app.SetVariable("SerialStatus", "OK: received data (no terminator) in " + elapsed + " ms");
                }
            }
            else
            {
                app.SetVariable("SerialResponse", "");
                app.SetVariable("SerialStatus", "Timeout: no data after " + timeoutMs.ToString() + " ms");
            }
        }
        catch (Exception ex)
        {
            app.SetVariable("SerialResponse", "");
            app.SetVariable("SerialStatus", "Error: " + ex.Message);
        }
        finally
        {
            try
            {
                if (port != null && port.IsOpen) port.Close();
            }
            catch { }
        }
	}
}
