#if !COMPANION_SAFE
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BigBlueFish.Agent;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    void AgentError(string title, Exception error)
    {
        var body = new StackPanel { Spacing = 15 }; body.Children.Add(Paragraph(error.Message));
        var dialog = Dialog(title, body); body.Children.Add(ActionButton("關閉", dialog.Close));
        if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
    }
    static async Task<string?> SelectAgentFolder(Window owner, string title, string initial)
    {
        var suggested = await owner.StorageProvider.TryGetFolderFromPathAsync(initial);
        var found = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = suggested });
        return found.FirstOrDefault()?.TryGetLocalPath();
    }
    Action BuildAgentWorkspaceSettings(StackPanel panel)
    {
        panel.Children.Add(Paragraph("工作區")); var path = Paragraph(""); panel.Children.Add(path);
        panel.Children.Add(Paragraph("檔案由各 Harness 共用；新的對話和專案也使用目前的資料夾。切換工作區會結束目前的要求。"));
        var buttons = new WrapPanel(); panel.Children.Add(buttons); bool operating = false;
        async Task Run(Func<Task> action)
        {
            if (operating) return; operating = true;
            try { await action(); } catch (OperationCanceledException) { }
            catch (Exception error) { AgentError("工作區操作失敗", error); }
            finally { operating = false; }
        }
        buttons.Children.Add(ActionButton("新增工作區", () => {
            if (agent == null || settings == null) return;
            var current = agent; string parent = Directory.GetParent(current.Workspaces.Current)?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var body = new StackPanel { Spacing = 12 }; body.Children.Add(Paragraph("建立一個獨立的資料夾，讓魚魚切換到這裡工作。"));
            var name = new TextBox { Text = "新工作區", PlaceholderText = "資料夾名稱" }; body.Children.Add(name);
            var pathText = Paragraph(parent); body.Children.Add(pathText);
            var dialog = Dialog("新增工作區", body, 650, 440);
            body.Children.Add(ActionButton("選擇位置", async () => { var selected = await SelectAgentFolder(dialog, "選擇新工作區的存放位置", parent); if (selected != null) { parent = selected; pathText.Text = parent; } }));
            var actions = new WrapPanel(); body.Children.Add(actions);
            actions.Children.Add(ActionButton("建立並開啟", async () => await Run(async () => { await current.CreateWorkspace(parent, name.Text ?? ""); dialog.Close(); })));
            actions.Children.Add(ActionButton("取消", dialog.Close)); dialog.Show(settings);
        }));
        buttons.Children.Add(ActionButton("開啟工作區", async () => {
            if (agent == null || settings == null) return;
            var current = agent; var selected = await SelectAgentFolder(settings, "選擇現有的工作區", current.Workspaces.Current);
            if (selected != null) await Run(() => current.OpenWorkspace(selected));
        }));
        buttons.Children.Add(ActionButton("開啟資料夾", () => {
            try { if (agent != null) { agent.Workspaces.EnsureCurrent(); OpenUrl(agent.Workspaces.Current); } }
            catch (Exception error) { AgentError("無法開啟資料夾", error); }
        }));
        buttons.Children.Add(ActionButton("清空工作區", async () => {
            if (agent == null) return; var current = agent; string expected = current.Workspaces.Current;
            await Run(async () => {
                current.Workspaces.ValidateClear(expected);
                if (await Confirm("清空工作區", $"將刪除下列工作區裡的所有檔案和子資料夾：\n\n{expected}\n\n清空後會重新開始魚魚對話，並重新準備預設的操作技能與人設。", "清空")) await current.ClearWorkspace(expected);
            });
        }));
        return () => { path.Text = agent?.Workspaces.Current ?? "連線後會準備預設的工作區。"; buttons.IsEnabled = agent != null && !operating && !agent.ChangingWorkspace; };
    }
    Action BuildAgentConfigurationSettings(StackPanel panel)
    {
        panel.Children.Add(Paragraph("模型與權限")); var rows = new StackPanel { Spacing = 8 }; panel.Children.Add(rows);
        panel.Children.Add(Paragraph("選項和適用範圍由目前的 Harness 提供；權限／工作模式決定它怎麼執行工作、何時要求你確認。"));
        AgentCoordinator? rendered = null; int revision = -1; bool applying = false; Action? refresh = null;
        async Task Apply(AgentCoordinator current, string id, string value)
        {
            if (applying) return; applying = true; rows.IsEnabled = false;
            try { await current.SetConfiguration(id, value); } catch (OperationCanceledException) { }
            catch (Exception error) { AgentError("Agent 設定沒有變更", error); }
            finally { applying = false; revision = -1; refresh?.Invoke(); }
        }
        refresh = () => {
            var current = agent; rows.IsEnabled = !applying && current?.Connected == true && !current.Busy && !current.ChangingWorkspace;
            if (applying || ReferenceEquals(rendered, current) && revision == (current?.Configuration.Revision ?? 0)) return;
            rendered = current; revision = current?.Configuration.Revision ?? 0; rows.Children.Clear();
            var options = current?.Configuration.Options ?? [];
            if (options.Length == 0) { rows.Children.Add(Paragraph(current?.Connected == true ? "模型、權限和工作模式由 Harness 設定；這個連線沒有提供可以調整的選項。" : "連線後會顯示 Agent 提供的模型、權限和思考選項。")); return; }
            if (!options.Any(o => o.Category == "model")) rows.Children.Add(Paragraph("目前模型：" + current!.CurrentModel));
            foreach (var option in options)
            {
                string label = option.Category switch { "model" => "模型 · ", "mode" => "權限／工作模式 · ", "thought_level" => "思考深度 · ", _ => "" };
                rows.Children.Add(Paragraph(label + option.Name));
                if (option.Description.Length > 0) rows.Children.Add(Paragraph(option.Description));
                if (option.Method.Length == 0) { rows.Children.Add(Paragraph(option.CurrentValue + " · 由 Harness 設定")); continue; }
                if (option.Type == "boolean")
                {
                    var toggle = new CheckBox { Content = option.Name, IsChecked = option.CurrentValue == "true" };
                    toggle.Click += async (_, _) => await Apply(current!, option.Id, toggle.IsChecked == true ? "true" : "false"); rows.Children.Add(toggle);
                }
                else
                {
                    var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    var choices = option.Choices.Select(c => { var item = new ComboBoxItem { Content = (c.Group.Length > 0 ? c.Group + " · " : "") + c.Name, Tag = c.Value }; ToolTip.SetTip(item, c.Description); return item; }).ToList();
                    var selected = choices.FirstOrDefault(i => (string?)i.Tag == option.CurrentValue);
                    if (selected == null) { selected = new ComboBoxItem { Content = option.CurrentValue.Length > 0 ? option.CurrentValue : "由 Harness 設定", Tag = option.CurrentValue, IsEnabled = false }; choices.Add(selected); }
                    selector.ItemsSource = choices; selector.SelectedItem = selected;
                    selector.SelectionChanged += async (_, _) => { if (selector.SelectedItem is ComboBoxItem { Tag: string value } && value != option.CurrentValue) await Apply(current!, option.Id, value); };
                    rows.Children.Add(selector);
                }
            }
        }; return refresh;
    }
}
#endif
