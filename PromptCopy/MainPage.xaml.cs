using CommunityToolkit.Maui.PlatformConfiguration.AndroidSpecific;
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

namespace PromptCopy
{
    public partial class MainPage : ContentPage
    {
        private static int m_hHook = 0;
        private HookProc m_HookProcedure;


        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x101;
        private const int WM_SYSKEYUP = 0x105;
        private int lastTicketOfCtrlC = 0;

        private void HookProcedure(int nCode, IntPtr wParam, IntPtr lParam)
        {

            int vkcode = Marshal.ReadInt32(lParam);
            var ctrlPressed = (wParam == (IntPtr)WM_KEYDOWN);

       
            if (ctrlPressed  && vkcode == (int)VirtualKeys.C)
            {
                if ((Environment.TickCount - lastTicketOfCtrlC) < 300) // less than ~300ms?
                {
                    Application.ActivateWindow()
                    Debug.Print("Hi");
                    
                }
                lastTicketOfCtrlC = Environment.TickCount;
                // Get previous selcetd text
            }
  
        }
            
        string modelPath = @"C:\phi3-model\directml\directml-int4-awq-block-128";
        Model model;
        Tokenizer tokenizer;
        Thread initThread;
        public MainPage()
        {
            InitializeComponent();
            langPicker.SelectedIndex = 4;
            m_HookProcedure = new HookProc(HookProcedure);
            m_hHook = SetWindowsHookEx(WH_KEYBOARD_LL, m_HookProcedure, GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0);

            this.translateBtn.IsEnabled = false;
            this.initThread = (new Thread(() =>
            {
                if (string.IsNullOrEmpty(modelPath))
                {
                    throw new Exception("Model path must be specified");
                }

                model = new Model(modelPath);
                tokenizer = new Tokenizer(model);

                MainThread.BeginInvokeOnMainThread(() => this.translateBtn.IsEnabled = true);
            })
            { IsBackground = true });
            this.initThread.Start();
        }

        private void OnCounterClicked(object sender, EventArgs e)
        {
            this.initThread.Join();
            translateBtn.IsEnabled = false;
            var target_lang = this.langPicker.SelectedItem;
            (new Thread(() =>
            {
                string example_case = @"Our models are not specifically designed or evaluated for all downstream purposes. Developers should consider common limitations of language models as they select use cases, and evaluate and mitigate for accuracy, safety, and fariness before using within a specific downstream use case, particularly for high risk scenarios. Developers should be aware of and adhere to applicable laws or regulations (including privacy, trade compliance laws, etc.) that are relevant to their use case.";
                string prompt =
                   $"Translate this to {target_lang} (but do not include any explanation)" + inputbox.Text;
                // richTextBox1.Text; // Example prompt
                var sequences = tokenizer.Encode($"<|user|>{prompt}<|end|><|assistant|>");



                using GeneratorParams generatorParams = new GeneratorParams(model);
                generatorParams.SetSearchOption("min_length", 50);
                generatorParams.SetSearchOption("max_length", inputbox.Text.Length * 2);
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
                    MainThread.BeginInvokeOnMainThread(() => outputbox.Text += nextWord);
                    
                }
                Console.WriteLine();
                watch.Stop();
                var runTimeInSeconds = watch.Elapsed.TotalSeconds;
                var outputSequence = generator.GetSequence(0);
                var totalTokens = outputSequence.Length;

                MainThread.BeginInvokeOnMainThread(() => outputbox.Text += ($"\nStreaming Tokens: {totalTokens} Time: {runTimeInSeconds:0.00} Tokens per second: {totalTokens / runTimeInSeconds:0.00}"));

            })
            { IsBackground = true }).Start();
            translateBtn.IsEnabled = true;
        }
    }

}
