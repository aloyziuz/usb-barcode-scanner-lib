﻿using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Text;

namespace BasselTech
{
    namespace UsbBarcodeScanner
    {
        public enum KeyboardKeyDirection
        {
            UP, DOWN
        }

        public class KeyStroke
        {
            public KeyboardKeyDirection Direction { get; set; }
            public Keys Key { get; set; }
        }

        public class BarcodeScannedEventArgs : EventArgs
        {
            public BarcodeScannedEventArgs(string barcode)
            {
                Barcode = barcode;
            }

            public string Barcode { get; }
        }

        public class UsbBarcodeScanner
        {
            #region WinAPI Declarations

            [DllImport("kernel32.dll")]
            private static extern IntPtr LoadLibrary(string lpLibFileName);

            [DllImport("user32.dll")]
            private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hInstance, uint threadId);

            [DllImport("user32.dll")]
            private static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

            [DllImport("user32.dll")]
            private static extern IntPtr GetKeyboardLayout(uint idThread);

            [DllImport("user32.dll")]
            static extern bool GetKeyboardState(byte[] lpKeyState);

            [DllImport("user32.dll")]
            static extern uint MapVirtualKey(uint uCode, uint uMapType);

            [DllImport("user32.dll")]
            static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

            #endregion

            #region Delegates and Constants

            private LowLevelKeyboardProc procDelegate;
            private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
            private const int WH_KEYBOARD_LL = 13;
            private const int WM_KEYDOWN = 0x0100;
            private const int WM_KEYUP = 0x0101;

            #endregion

            #region Private Fields

            private static IntPtr _hookId = IntPtr.Zero;
            private readonly List<KeyStroke> _keys = new List<KeyStroke>();
            private readonly Timer _timer = new Timer();

            #endregion

            #region Events

            public event EventHandler<BarcodeScannedEventArgs> BarcodeScanned;

            #endregion

            #region Constructor

            public UsbBarcodeScanner(int scannerTypingInterval = 20)
            {
                this.procDelegate = KeyboardHookCallback;
                _timer.Interval = scannerTypingInterval;
                _timer.Tick += (sender, args) => {
                    _keys.Clear(); 
                };
                _timer.Stop();
            }

            public void Start()
            {
                if (IsCapturing())
                    return;
                _hookId = SetHook(this.procDelegate);
                _timer.Start();
            }

            public void Stop()
            {
                if (!IsCapturing())
                    return;
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _timer.Stop();
                _keys.Clear();
            }

            public bool IsCapturing()
            {
                return _hookId != IntPtr.Zero;
            }

            #endregion

            #region Private Methods

            private IntPtr SetHook(LowLevelKeyboardProc proc)
            {
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    return SetWindowsHookEx(WH_KEYBOARD_LL, proc, LoadLibrary("user32"), 0);
                }
            }

            private string GetBarcodeString()
            {
                var barcodeBuilder = new StringBuilder();
                var shiftFlag = false;
                                
                foreach (var keystroke in _keys)
                {
                    var isShiftKey = IsShiftKey(keystroke.Key);

                    //if shift is pressed, apply shift flag
                    if (isShiftKey && keystroke.Direction == KeyboardKeyDirection.DOWN)
                    {
                        shiftFlag = true;
                        continue;
                    }
                    //if shift key is released, remove shift flag
                    else if (isShiftKey && keystroke.Direction == KeyboardKeyDirection.UP)
                    {
                        shiftFlag = false;
                        continue;
                    }

                    barcodeBuilder.Append(KeyCodeToUnicode(keystroke.Key, shiftFlag));
                }

                return barcodeBuilder.ToString();
            }

            private string KeyCodeToUnicode(Keys key, bool shiftFlag)
            {
                var lpKeyState = new byte[255];
                GetKeyboardState(lpKeyState);

                if (shiftFlag)
                    lpKeyState[(int)Keys.ShiftKey] = 0x80;

                var wVirtKey = (uint)key;
                var wScanCode = MapVirtualKey(wVirtKey, 0);

                var pwszBuff = new StringBuilder();
                ToUnicodeEx(wVirtKey, wScanCode, lpKeyState, pwszBuff, 5, 0, GetKeyboardLayout(0));

                return pwszBuff.ToString();
            }

            private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
                {
                    var key = this.GetKey(lParam);
                    _timer.Stop();

                    if (key == Keys.Enter)
                    {
                        if (_keys.Count > 0)
                        {
                            var barcode = GetBarcodeString();
                            if(!string.IsNullOrWhiteSpace(barcode))
                                BarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(barcode));
                        }
                        _keys.Clear();
                    }
                    else
                    {
                        _keys.Add(new KeyStroke{ Direction = KeyboardKeyDirection.DOWN, Key = key});
                        _timer.Start();
                    }
                }
                else if(nCode >= 0 && wParam == (IntPtr)WM_KEYUP)
                {
                    var key = this.GetKey(lParam);

                    //record shift key release event so that we can determine which keys are capital
                    if (this.IsShiftKey(key))
                    {
                        this._keys.Add(new KeyStroke { Direction = KeyboardKeyDirection.UP, Key = key });
                    }
                }

                return IntPtr.Zero;
            }

            private Keys GetKey(IntPtr lParam){
                // Extract the virtual key code
                var vkCode = Marshal.ReadInt32(lParam);

                // Convert the virtual key code to a Keys enum value
                var key = (Keys)vkCode;
                return key;
            }

            private bool IsShiftKey(Keys keystroke)
            {
                return keystroke == Keys.ShiftKey || keystroke == Keys.LShiftKey ||
                                        keystroke == Keys.RShiftKey;
            }

            #endregion

            #region Destructor

            ~UsbBarcodeScanner()
            {
                Stop();
            }

            #endregion
        }

    }
}
