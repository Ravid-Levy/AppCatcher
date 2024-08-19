using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Catcher
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool CloseWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool IsWindow(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // Declare the hook
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_CALLWNDPROC = 4;
        private HookProc _hookProc;
        private IntPtr _hookID = IntPtr.Zero;
        private Dictionary<IntPtr, TabPage> _windowTabs = new Dictionary<IntPtr, TabPage>();

        public Form1()
        {
            InitializeComponent();

            _hookProc = new HookProc(HookCallback);
            _hookID = SetHook(_hookProc);
            tabControl1.AllowDrop = true;
            tabControl1.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl1.DrawItem += TabControl1_DrawItem;
            tabControl1.MouseDown += TabControl1_MouseDown;
        }

        private IntPtr SetHook(HookProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_CALLWNDPROC, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = Marshal.PtrToStructure<CWPSTRUCT>(lParam);

                if (msg.message == 0x0010) // WM_CLOSE
                {
                    if (_windowTabs.ContainsKey(msg.hwnd))
                    {
                        this.Invoke(new Action(() =>
                        {
                            tabControl1.TabPages.Remove(_windowTabs[msg.hwnd]);
                            _windowTabs.Remove(msg.hwnd);
                        }));
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
            base.OnFormClosing(e);
        }



        private void ListWindows()
        {
            List<IntPtr> windowHandles = new List<IntPtr>();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    StringBuilder windowText = new StringBuilder(256);
                    GetWindowText(hWnd, windowText, windowText.Capacity);

                    if (windowText.Length > 0)
                    {
                        windowHandles.Add(hWnd);
                    }
                }
                return true;
            }, IntPtr.Zero);

            using (Form selectionForm = new Form())
            {
                selectionForm.Text = "Select a Window to Embed";
                ListBox listBox = new ListBox { Dock = DockStyle.Fill };
                listBox.DisplayMember = "Text";

                foreach (IntPtr hWnd in windowHandles)
                {
                    StringBuilder windowText = new StringBuilder(256);
                    GetWindowText(hWnd, windowText, windowText.Capacity);
                    listBox.Items.Add(new { Handle = hWnd, Text = windowText.ToString() });
                }

                listBox.DoubleClick += (sender, e) =>
                {
                    if (listBox.SelectedItem != null)
                    {
                        dynamic selectedItem = listBox.SelectedItem;
                        IntPtr selectedHandle = selectedItem.Handle;

                        try
                        {
                            TabPage tabPage = new TabPage(selectedItem.Text);
                            tabPage.Tag = selectedHandle;
                            tabControl1.TabPages.Add(tabPage);
                            _windowTabs[selectedHandle] = tabPage;

                            tabPage.Resize += (s, ev) =>
                            {
                                MoveWindow(selectedHandle, 0, 0, tabPage.ClientSize.Width, tabPage.ClientSize.Height, true);
                            };

                            IntPtr result = SetParent(selectedHandle, tabPage.Handle);

                            if (result == IntPtr.Zero)
                            {
                                MessageBox.Show("Failed to embed the window. This may be a system window or a protected window.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                tabControl1.TabPages.Remove(tabPage);
                                return;
                            }

                            MoveWindow(selectedHandle, 0, 0, tabPage.ClientSize.Width, tabPage.ClientSize.Height, true);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }

                        selectionForm.Close();
                    }
                };

                selectionForm.Controls.Add(listBox);
                selectionForm.ShowDialog();
            }
        }

        private void TabControl1_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabPage tabPage = tabControl1.TabPages[e.Index];
            Rectangle rect = e.Bounds;
            rect.Inflate(-2, -2);

            e.Graphics.DrawString(tabPage.Text, this.Font, Brushes.Black, rect.X, rect.Y);

            Rectangle detachButton = new Rectangle(rect.Right - 30, rect.Y, 15, 15);
            e.Graphics.DrawString("D", this.Font, Brushes.Red, detachButton);

            Rectangle closeButton = new Rectangle(rect.Right - 15, rect.Y, 15, 15);
            e.Graphics.DrawString("X", this.Font, Brushes.Red, closeButton);
        }

        private void TabControl1_MouseDown(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < tabControl1.TabPages.Count; i++)
            {
                Rectangle rect = tabControl1.GetTabRect(i);
                Rectangle detachButton = new Rectangle(rect.Right - 30, rect.Y, 15, 15);
                Rectangle closeButton = new Rectangle(rect.Right - 15, rect.Y, 15, 15);

                if (detachButton.Contains(e.Location))
                {
                    IntPtr hWnd = (IntPtr)tabControl1.TabPages[i].Tag;
                    DetachWindow(hWnd);
                    tabControl1.TabPages.RemoveAt(i);
                    _windowTabs.Remove(hWnd);
                    break;
                }
                else if (closeButton.Contains(e.Location))
                {
                    IntPtr hWnd = (IntPtr)tabControl1.TabPages[i].Tag;
                    CloseEmbeddedWindow(hWnd);
                    tabControl1.TabPages.RemoveAt(i);
                    _windowTabs.Remove(hWnd);
                    break;
                }
            }
        }

        private void DetachWindow(IntPtr hWnd)
        {
            SetParent(hWnd, IntPtr.Zero);
        }

        private void CloseEmbeddedWindow(IntPtr hWnd)
        {
            CloseWindow(hWnd);
        }

        private void TabControl1_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Link;
        }

        private void TabControl1_DragDrop(object sender, DragEventArgs e)
        {
            IntPtr hWnd = (IntPtr)e.Data.GetData(DataFormats.Text);
            if (hWnd != IntPtr.Zero)
            {
                TabPage tabPage = new TabPage("Dragged Window");
                tabPage.Tag = hWnd;
                tabControl1.TabPages.Add(tabPage);
                _windowTabs[hWnd] = tabPage;

                tabPage.Resize += (s, ev) =>
                {
                    MoveWindow(hWnd, 0, 0, tabPage.ClientSize.Width, tabPage.ClientSize.Height, true);
                };

                IntPtr result = SetParent(hWnd, tabPage.Handle);

                if (result == IntPtr.Zero)
                {
                    MessageBox.Show("Failed to embed the window. This may be a system window or a protected window.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    tabControl1.TabPages.Remove(tabPage);
                    return;
                }

                MoveWindow(hWnd, 0, 0, tabPage.ClientSize.Width, tabPage.ClientSize.Height, true);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CWPSTRUCT
        {
            public IntPtr lParam;
            public IntPtr wParam;
            public uint message;
            public IntPtr hwnd;
        }


        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            ListWindows();

        }
    }
}
