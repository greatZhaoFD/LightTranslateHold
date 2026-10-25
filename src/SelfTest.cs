using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LightTranslateHold
{
    internal static class SelfTest
    {
        public static int Run()
        {
            StringBuilder report = new StringBuilder();
            int failures = 0;
            try
            {
                Settings s = new Settings();
                s.ApiKey = "self-test-placeholder";
                Check(s.ApiKey == "self-test-placeholder", "DPAPI key round-trip", report, ref failures);
                Check(s.ProtectedKey != "self-test-placeholder" && s.ProtectedKey.Length > 20, "DPAPI key not plaintext", report, ref failures);
                Check(s.TriggerKeyCode == Keys.D1 && s.TriggerModifiers == Hotkey.MOD_ALT, "default trigger Alt+1", report, ref failures);
                Keys hk;
                int hm;
                string he;
                Check(Hotkey.TryParse("Alt+1", out hk, out hm, out he) && hk == Keys.D1 && hm == Hotkey.MOD_ALT, "parse Alt+1", report, ref failures);
                Check(Hotkey.NormalizeOrFallback("Alt+1") == "Alt+1", "normalize Alt+1 round-trip", report, ref failures);
                Check(Hotkey.NormalizeOrFallback("Ctrl+Shift+F9") == "Ctrl+Shift+F9", "normalize Ctrl+Shift+F9 round-trip", report, ref failures);
                Check(Hotkey.TryParse("F8", out hk, out hm, out he) && hk == Keys.F8 && hm == 0, "parse F8", report, ref failures);
                Check(!Hotkey.TryParse("1", out hk, out hm, out he), "reject bare character key", report, ref failures);

                TranslationDirection zh = TranslationDirection.Detect("这是一个中文句子，带一个 English 术语。", "auto");
                Check(zh.Target == "en", "Chinese -> English", report, ref failures);
                Check(zh.SystemPrompt.IndexOf("英文", StringComparison.Ordinal) >= 0, "Chinese prompt asks English", report, ref failures);
                TranslationDirection en = TranslationDirection.Detect("The quick brown fox jumps over the lazy dog.", "auto");
                Check(en.Target == "zh", "English -> Chinese", report, ref failures);
                Check(en.SystemPrompt.IndexOf("简体中文", StringComparison.Ordinal) >= 0, "English prompt asks Chinese", report, ref failures);
                Check(TranslationDirection.Detect("Hello", "en").Target == "en", "fixed English target", report, ref failures);
                Check(TranslationDirection.Detect("你好", "zh").Target == "zh", "fixed Chinese target", report, ref failures);

                Uri baseUri = Translator.Address("https://api.deepseek.com");
                Check(baseUri.AbsoluteUri.EndsWith("/chat/completions", StringComparison.Ordinal), "base URL normalization", report, ref failures);
                Uri fullUri = Translator.Address("https://api.openai.com/v1/chat/completions");
                Check(fullUri.AbsoluteUri.EndsWith("/v1/chat/completions", StringComparison.Ordinal), "full endpoint preserved", report, ref failures);
                bool insecureRejected = false;
                try { Translator.Address("http://example.com/v1"); } catch { insecureRejected = true; }
                Check(insecureRejected, "non-loopback HTTP rejected", report, ref failures);
                bool loopbackAccepted = false;
                try { Translator.Address("http://127.0.0.1:11434/v1"); loopbackAccepted = true; } catch { }
                Check(loopbackAccepted, "loopback HTTP accepted", report, ref failures);

                string json = "{\"choices\":[{\"delta\":{\"content\":\"你好\"},\"finish_reason\":\"stop\"}]}";
                Check(JsonUtil.Delta(JsonUtil.Object(json)) == "你好", "SSE delta parse", report, ref failures);
                Check(JsonUtil.FinishReason(JsonUtil.Object(json)) == "stop", "finish_reason parse", report, ref failures);

                using (MockServer server = new MockServer("HTTP/1.1 200 OK", "text/event-stream",
                    "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n"
                    + "data: {\"choices\":[{\"delta\":{\"content\":\"好\"},\"finish_reason\":\"stop\"}]}\n\n"
                    + "data: [DONE]\n\n"))
                {
                    Settings mock = new Settings();
                    mock.Endpoint = "http://127.0.0.1:" + server.Port + "/v1";
                    mock.Model = "mock-model";
                    mock.ApiKey = "test";
                    StringBuilder result = new StringBuilder();
                    using (Translator t = new Translator())
                    {
                        t.TranslateAsync(mock, "Hello", TranslationDirection.Detect("Hello", "auto"),
                            delegate(string piece) { result.Append(piece); }, CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    Check(result.ToString() == "你好", "streaming translation end-to-end (mock API)", report, ref failures);
                }

                using (MockServer server = new MockServer("HTTP/1.1 401 Unauthorized", "application/json",
                    "{\"error\":{\"message\":\"invalid key\"}}"))
                {
                    Settings mock = new Settings();
                    mock.Endpoint = "http://127.0.0.1:" + server.Port + "/v1";
                    mock.Model = "mock-model";
                    mock.ApiKey = "bad";
                    bool unauthorized = false;
                    try
                    {
                        using (Translator t = new Translator())
                        {
                            t.TranslateAsync(mock, "Hello", TranslationDirection.Detect("Hello", "auto"),
                                delegate(string piece) { }, CancellationToken.None).GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        unauthorized = ex.Message.IndexOf("401", StringComparison.Ordinal) >= 0;
                    }
                    Check(unauthorized, "HTTP 401 error mapping", report, ref failures);
                }
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine("FAIL: unhandled exception: " + ex);
            }

            report.AppendLine();
            report.AppendLine(failures == 0 ? "ALL TESTS PASSED" : ("FAILURES: " + failures));
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(Path.Combine(AppPaths.DataDir, "self-test.txt"), report.ToString(), new UTF8Encoding(false));
            }
            catch { }
            return failures == 0 ? 0 : 1;
        }

        private static void Check(bool condition, string name, StringBuilder report, ref int failures)
        {
            if (condition)
            {
                report.AppendLine("PASS: " + name);
            }
            else
            {
                failures++;
                report.AppendLine("FAIL: " + name);
            }
        }

        private sealed class MockServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly string _status;
            private readonly string _contentType;
            private readonly string _body;
            public int Port;

            public MockServer(string status, string contentType, string body)
            {
                _status = status;
                _contentType = contentType;
                _body = body;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(AcceptLoop);
                _thread.IsBackground = true;
                _thread.Start();
            }

            private void AcceptLoop()
            {
                try
                {
                    using (TcpClient client = _listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        ReadRequest(stream);
                        byte[] body = Encoding.UTF8.GetBytes(_body);
                        string headers = _status + "\r\n"
                            + "Content-Type: " + _contentType + "\r\n"
                            + "Content-Length: " + body.Length + "\r\n"
                            + "Connection: close\r\n\r\n";
                        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                        stream.Write(headerBytes, 0, headerBytes.Length);
                        stream.Write(body, 0, body.Length);
                        stream.Flush();
                    }
                }
                catch { }
            }

            private static void ReadRequest(NetworkStream stream)
            {
                MemoryStream ms = new MemoryStream();
                byte[] buffer = new byte[4096];
                int headerEnd = -1;
                while (headerEnd < 0)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) return;
                    ms.Write(buffer, 0, read);
                    byte[] data = ms.ToArray();
                    headerEnd = IndexOf(data, new byte[] { 13, 10, 13, 10 });
                }
                byte[] all = ms.ToArray();
                string header = Encoding.ASCII.GetString(all, 0, headerEnd);
                int contentLength = 0;
                foreach (string line in header.Split(new string[] { "\r\n" }, StringSplitOptions.None))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        Int32.TryParse(line.Substring(15).Trim(), out contentLength);
                    }
                }
                int already = all.Length - (headerEnd + 4);
                int remaining = contentLength - already;
                while (remaining > 0)
                {
                    int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read <= 0) break;
                    remaining -= read;
                }
            }

            private static int IndexOf(byte[] data, byte[] pattern)
            {
                for (int i = 0; i <= data.Length - pattern.Length; i++)
                {
                    bool ok = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (data[i + j] != pattern[j]) { ok = false; break; }
                    }
                    if (ok) return i;
                }
                return -1;
            }

            public void Dispose()
            {
                try { _listener.Stop(); } catch { }
                try { _thread.Join(2000); } catch { }
            }
        }
    }
}
