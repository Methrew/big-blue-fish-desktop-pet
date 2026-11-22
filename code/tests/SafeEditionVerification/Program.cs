// Modified by acer1204 on 2026-10-30 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BigBlueFish.Windows;

static class Program
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T value) yield return value; foreach(var nested in Descendants<T>(child)) yield return nested; } }
    static void Pump(int ms) { var frame=new DispatcherFrame(); var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(ms)}; timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame); }
    [STAThread] static int Main(string[] args)
    {
        MainWindow? pet=null;
        try {
            string path=Path.GetFullPath(args[0]);Directory.CreateDirectory(path);
            File.Copy(Path.Combine(AppContext.BaseDirectory,"BigBlueFish.ico"),Path.Combine(path,"BigBlueFish.ico"),true);
            // Brushed already today: between 23:00 and midnight AnimationBoundary would otherwise play the daily brushing instead of idle.
            string today=DateTime.Now.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(path,"preferences.json"),"{\"ModelEnabled\":false,\"PublicInfo\":false,\"CheckUpdatesOnStartup\":false,\"McpEnabled\":true,\"McpChatEnabled\":true,\"AgentEnabled\":true,\"AgentDailySpeech\":true,\"PuppetMode\":true,\"LastBrushDate\":\""+today+"\"}");
            App.Args=["--test-mode"];var app=new System.Windows.Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Environment.CurrentDirectory=path;
            pet=new MainWindow();pet.Show();Pump(500);
            object Read(string name)=>pet.GetType().GetField(name,Access)!.GetValue(pet)!;
            object? Invoke(string name,params object[] values)=>pet.GetType().GetMethod(name,Access)!.Invoke(pet,values);
            var assembly=typeof(MainWindow).Assembly;
            Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name is "BigBlueFish.Control" or "BigBlueFish.Agent"),"safe executable has no Control or Agent assembly reference");
            Check(pet.GetType().GetMethod("ExecuteMcp",Access)==null,"safe executable contains no MCP executor");
            var prefs=Read("preferences");
            bool Flag(string name)=>(bool)prefs.GetType().GetProperty(name)!.GetValue(prefs)!;
            Check(!Flag("McpEnabled") && !Flag("AgentEnabled") && !Flag("PuppetMode") && !Flag("McpChatEnabled") && !Flag("AgentDailySpeech"),"old enabled flags cannot start MCP, the Agent connection, chat or puppet in Safe");
            Check((string)Read("animationPhase")=="Startup","safe keeps ordinary welcome animation");
            Invoke("OpenCompanionSettings");Pump(150);var settings=(Window)Read("companionSettings");
            Check(!Descendants<Button>(settings).Any(b=>b.Content?.ToString() is "Agent 與 MCP" or "連線 Agent"),"safe settings have no Agent connection or MCP page");
            // The sidebar subtitle was cut to "1.1.5-tw.1 · 無MCP版 · 陪" on Windows 11 (2026-11-22): every line has to fit.
            var subtitle=Descendants<TextBlock>(settings).Single(t=>t.Text.EndsWith("陪伴設定",StringComparison.Ordinal));
            var face=new Typeface(subtitle.FontFamily,subtitle.FontStyle,subtitle.FontWeight,subtitle.FontStretch);
            double LineWidth(string line)=>new FormattedText(line,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,face,subtitle.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(subtitle).PixelsPerDip).WidthIncludingTrailingWhitespace;
            string version=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            Check(subtitle.Text==version+" · 無MCP版\n陪伴設定"&&subtitle.Text.Split('\n').All(line=>LineWidth(line)<=subtitle.ActualWidth+.5),"safe settings subtitle shows the full version, the edition and 陪伴設定 without clipping");
            Descendants<Button>(settings).Single(b=>b.Content?.ToString()=="記錄主控台").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(350);
            Check(Descendants<TextBox>(settings).Any(t=>t.Name=="LogConsoleText"&&t.IsReadOnly&&t.Text.Contains("SettingsOpened")),"safe edition has a working shareable log console without loading Agent");
            settings.Close();Invoke("AnimationBoundary");
            Check((string)Read("animationPhase")=="Idle","safe keeps ordinary idle logic");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BigBlueFish.Control"),"no MCP assembly loaded in actual process");
            pet.GetType().GetField("exitReady",Access)!.SetValue(pet,true);pet.Close();
            Console.WriteLine($"Safe actual WPF integration: {checks} checks passed.");return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);if(pet!=null){pet.GetType().GetField("exitReady",Access)!.SetValue(pet,true);pet.Close();}return 1; }
    }
}
