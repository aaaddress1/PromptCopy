using CommunityToolkit.Maui.Core.Platform;
using CommunityToolkit.Maui.PlatformConfiguration.AndroidSpecific;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using static PromptCopy.WinAPI;
using static System.Net.Mime.MediaTypeNames;

namespace PromptCopy
{
    public partial class MainPage : ContentPage
    {
        private static int m_hHook = 0;
        private HookProc m_HookProcedure;

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(System.Drawing.Point pt);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out System.Drawing.Point pt);

        public static IntPtr GetWindowUnderCursor()
        {
            System.Drawing.Point ptCursor;
            if (!GetCursorPos(out ptCursor))
                return IntPtr.Zero;
            return WindowFromPoint(ptCursor);
        }

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        private void SendCtrlC(IntPtr hWnd)
        {
            uint KEYEVENTF_KEYUP = 2;
            byte VK_CONTROL = 0x11;
            SetForegroundWindow((int)hWnd);
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(0x43, 0, 0, 0); //Send the C key (43 is "C")
            keybd_event(0x43, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);// 'Left Control Up

        }


        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x101;
        private const int WM_SYSKEYUP = 0x105;
        private int lastTicketOfCtrlC = 0;
        private int lastTicketOfCtrl = 0;
        [DllImport("User32.dll")]
        public static extern Int32 SetForegroundWindow(int hWnd);
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // 引入 user32.dll 的 ShowWindow 函數
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // 常量，用於設置窗口顯示狀態
        public const int SW_SHOW = 5;
        public const int SW_HIDE = 0;


        public class ClipboardManager
        {
            [DllImport("user32.dll")]
            private static extern IntPtr GetClipboardData(uint uFormat);

            [DllImport("user32.dll")]
            private static extern bool IsClipboardFormatAvailable(uint format);

            [DllImport("user32.dll")]
            private static extern bool OpenClipboard(IntPtr hWndNewOwner);

            [DllImport("user32.dll")]
            private static extern bool CloseClipboard();

            private const uint CF_TEXT = 1;

            public static string GetClipboardText()
            {
                if (!OpenClipboard(IntPtr.Zero))
                    return null;

                string clipboardText = null;

                if (IsClipboardFormatAvailable(CF_TEXT))
                {
                    IntPtr handle = GetClipboardData(CF_TEXT);
                    if (handle != IntPtr.Zero)
                    {
                        IntPtr pointer = Marshal.StringToHGlobalAnsi(Marshal.PtrToStringAnsi(handle));
                        clipboardText = Marshal.PtrToStringAnsi(pointer);
                        Marshal.FreeHGlobal(pointer);
                    }
                }

                CloseClipboard();

                return clipboardText;
            }
        }

        private int HookProcedure(int nCode, IntPtr wParam, IntPtr lParam)
        {

            int vkcode = Marshal.ReadInt32(lParam);
            var funckeyPressDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
            var funckeyPressUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

            bool ctrlPressed = (GetAsyncKeyState((int)VirtualKeys.Control) & 0x8000) != 0;
            bool altPressed = (GetAsyncKeyState((int)VirtualKeys.Menu) & 0x8000) != 0;
            bool shiftPressed = (GetAsyncKeyState((int)VirtualKeys.LeftShift) & 0x8000) != 0;


            Debug.Print($"[{funckeyPressDown.ToString()}/{funckeyPressUp.ToString()}] VK:{vkcode} nCode: {nCode.ToString()}, ctrl: {ctrlPressed.ToString()}");

            if (funckeyPressDown && ctrlPressed && vkcode == (int)VirtualKeys.Return && this.selected_prompt.IsFocused)
            {
                this.translateInvoke();
                return 1;
            }
            if (funckeyPressDown && ctrlPressed && vkcode == (int)VirtualKeys.C)
            {
                if ((Environment.TickCount - lastTicketOfCtrlC) < 300) // less than ~300ms?
                {

                    Debug.Print("double copy");

                    // var v = Process.GetCurrentProcess().MainWindowHandle;
                    //var hwnd = ((MauiWinUIWindow)App.Current.Windows[0].Handler.PlatformView).WindowHandle;

                    var clipText = ClipboardManager.GetClipboardText();
                    this.inputbox.Text = clipText;

                    statusbar.Text = $"Copied {inputbox.Text.Length} Text - Press [Ctrl] + [Enter] to finish prompt to run AI analyze.";

                    // if (this.translateBtn.IsEnabled) this.translateClicked(this.translateBtn, EventArgs.Empty);
#if WINDOWS
                    var currWin = ((MauiWinUIWindow)this.Window.Handler.PlatformView);
                    var hwnd = currWin.WindowHandle;
                    ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow((int)hwnd);
#endif
                    this.selected_prompt.Focus();
                }
                lastTicketOfCtrlC = Environment.TickCount;
            }

            if (funckeyPressUp && ctrlPressed && (vkcode == (int)VirtualKeys.LeftControl || vkcode == (int)VirtualKeys.RightControl))
            {
                if ((Environment.TickCount - lastTicketOfCtrl) < 300) // less than ~300ms?
                {
                    Debug.Print("double ctrl");
                    var clipTextBefore = ClipboardManager.GetClipboardText(); 

                    // Get the selected text
                    Thread.Sleep(20);
                    SendCtrlC(GetWindowUnderCursor());
                    Thread.Sleep(30);
                    var clipTextAfter = ClipboardManager.GetClipboardText();
                    Clipboard.SetTextAsync(clipTextBefore);

                    this.inputbox.Text = clipTextAfter;

                    Debug.Print("double control");
#if WINDOWS
                    var currWin = ((MauiWinUIWindow)this.Window.Handler.PlatformView);
                    var hwnd = currWin.WindowHandle;
                    ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow((int)hwnd);
#endif
                }
                lastTicketOfCtrl = Environment.TickCount;
            }
            return CallNextHookEx(0, nCode, wParam, lParam);
        }

        string modelPath = @"C:\phi3-model\directml\directml-int4-awq-block-128";
        Model model;
        Tokenizer tokenizer;
        Thread initThread;
        public MainPage()
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);
            //langPicker.SelectedIndex = 4;
            m_HookProcedure = new HookProc(HookProcedure);
            m_hHook = SetWindowsHookEx(WH_KEYBOARD_LL, m_HookProcedure, GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0);

            this.inputbox.IsEnabled = false;
            this.initThread = (new Thread(() =>
            {
                if (string.IsNullOrEmpty(modelPath))
                {
                    throw new Exception("Model path must be specified");
                }

                model = new Model(modelPath);
                tokenizer = new Tokenizer(model);

                MainThread.BeginInvokeOnMainThread(() => this.inputbox.IsEnabled = true);
            })
            { IsBackground = true });
            this.initThread.Start();
        }

        private void translateInvoke()
        {
            if (inputbox.Text.Length < 5)
            {
                statusbar.Text = $"At least 10 words as input to analyze, now only got {inputbox.Text.Length} tokens.";
                return;
            }
            this.initThread.Join();
            inputbox.IsEnabled = false;
            statusbar.Text = $"Processing user query (length {inputbox.Text.Length} tokens)";
            //var target_lang = this.langPicker.SelectedItem;
            (new Thread(() =>
            {
                //string sysprompt = $"All your response can only write in {target_lang} language";
                //                var sequences = tokenizer.Encode($@"<|system|>{sysprompt}<|end|><|user|>{selected_prompt.Text}:\r\n{inputbox.Text}<|end|><|assistant|>");
                var sequences = tokenizer.Encode($@"<|user|>{selected_prompt.Text}:\r\n{inputbox.Text}<|end|><|assistant|>");


                using GeneratorParams generatorParams = new GeneratorParams(model);
                generatorParams.SetSearchOption("min_length", 5);
                generatorParams.SetSearchOption("max_length", inputbox.Text.Length * 2);
                //generatorParams.SetSearchOption("temperature", 0.3); // 設置生成的隨機性
                //generatorParams.SetSearchOption("top_k", 50);        // 設置Top-k取樣

                generatorParams.SetInputSequences(sequences);

                using var tokenizerStream = tokenizer.CreateStream();
                using var generator = new Generator(model, generatorParams);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                MainThread.BeginInvokeOnMainThread(() => outputbox.Text = "");

                while (!generator.IsDone())
                {
                    generator.ComputeLogits();
                    generator.GenerateNextToken();
                    var nextWord = tokenizerStream.Decode(generator.GetSequence(0)[^1]);
                    MainThread.BeginInvokeOnMainThread(() => {
                        outputbox.Text += nextWord;
                        var progress = (int)(100 * outputbox.Text.Length / inputbox.Text.Length);
                        statusbar.Text = $"Progress ... {progress}% ";
                    });
                }
                Console.WriteLine();
                watch.Stop();
                var runTimeInSeconds = watch.Elapsed.TotalSeconds;
                var outputSequence = generator.GetSequence(0);
                var totalTokens = outputSequence.Length;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    statusbar.Text = ($"Streaming Tokens: {totalTokens} Time: {runTimeInSeconds:0.00}\nTokens per second: {totalTokens / runTimeInSeconds:0.00}");
                    inputbox.IsEnabled = true;
                });
            })
            { IsBackground = true }).Start();

        }

        private void inputbox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }

}
