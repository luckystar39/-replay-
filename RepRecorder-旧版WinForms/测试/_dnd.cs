// _dnd.cs -- temporary test helper: performs a REAL OLE drag & drop onto another window.
// usage: _dnd.exe <screenX> <screenY> <file1> [file2 ...]
//
// Notes learned the hard way:
//   * GetKeyState() reads the calling thread's *synchronized* key state, which is only
//     refreshed while that thread pumps messages. Synthesize the button-down, then pump
//     (Application.DoEvents) before calling DoDragDrop -- otherwise OLE thinks no button
//     is held and the drag loop never ends.
//   * a small mouse move is needed for OLE to pass its drag threshold.
//   * always self-destruct after a timeout so a stuck drag cannot wedge the test run.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class Dnd
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, int d, IntPtr e);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: _dnd.exe x y file..."); return; }
        int x = int.Parse(args[0]);
        int y = int.Parse(args[1]);
        string[] files = new string[args.Length - 2];
        Array.Copy(args, 2, files, 0, files.Length);

        Application.OleRequired();

        Form f = new Form();
        f.ShowInTaskbar = false;
        f.FormBorderStyle = FormBorderStyle.None;
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.Size = new Size(10, 10);
        f.Show();
        Application.DoEvents();

        // hard stop: never let a wedged drag hold the test harness hostage
        Thread killer = new Thread(delegate()
        {
            Thread.Sleep(12000);
            Console.WriteLine("TIMEOUT: drag never completed");
            Console.Out.Flush();
            Environment.Exit(3);
        });
        killer.IsBackground = true;
        killer.Start();

        Thread.Sleep(500);
        SetCursorPos(x, y);
        Thread.Sleep(500);

        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);   // LBUTTONDOWN
        Thread.Sleep(200);
        Application.DoEvents();                      // let this thread's key state sync
        SetCursorPos(x + 2, y + 2);                  // clear OLE's drag threshold
        Thread.Sleep(80);
        Application.DoEvents();
        SetCursorPos(x, y);
        Thread.Sleep(80);
        Application.DoEvents();

        Thread t = new Thread(delegate()
        {
            Thread.Sleep(1000);
            mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);   // LBUTTONUP -> OLE drops
        });
        t.IsBackground = true;
        t.Start();

        DataObject data = new DataObject(DataFormats.FileDrop, files);
        DragDropEffects eff = f.DoDragDrop(data, DragDropEffects.Copy);
        Console.WriteLine("drop effect = " + eff);
        Console.Out.Flush();

        for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(30); }
        f.Close();
        Environment.Exit(0);
    }
}
