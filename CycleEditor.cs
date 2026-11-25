namespace BAAxis;

internal sealed partial class MainForm
{
    private readonly ComboBox endMode = new() { DropDownStyle=ComboBoxStyle.DropDownList };
    private readonly TextBox endWait = new() { Text="00:02.000" };
    private void BuildEndControls(Control settings)
    {
        settings.Height=430;
        settings.Controls.Add(new Label { Text="收轴方式",AutoSize=true,Location=new Point(20,284) });
        endMode.Items.AddRange(["按战斗剩余时间", "全部操作结束后等待"]);
        endMode.SetBounds(20,310,208,30);settings.Controls.Add(endMode);
        settings.Controls.Add(new Label { Text="操作结束后等待（分:秒.毫秒）",AutoSize=true,Location=new Point(20,350) });
        endWait.SetBounds(20,378,208,30);settings.Controls.Add(endWait);
        endMode.SelectedIndexChanged+=(_,_)=> { end.Enabled=endMode.SelectedIndex==0;endWait.Enabled=endMode.SelectedIndex==1; };
    }
    private void ReadEndControls()
    {
        document.EndMode=endMode.SelectedIndex==1?EndCheckMode.AfterLastAction:EndCheckMode.Remaining;
        document.EndWaitMs=AxisDocument.Parse(endWait.Text);
        if(document.EndWaitMs<0) throw new ArgumentException("等待时长应大于或等于零。");
    }
    private void BindEndControls()
    {
        endMode.SelectedIndex=document.EndMode==EndCheckMode.AfterLastAction?1:0;
        endWait.Text=AxisDocument.Format(document.EndWaitMs);
    }
    private void MarkBattleStart()
    {
        Idle();ReadFields();
        if(EventAt(grid.CurrentCell?.RowIndex ?? -1) is not AxisEvent row) throw new ArgumentException("请先选中跳过动画确认键按下的那一行。");
        long offset=AxisDocument.Parse(document.Total)-AxisDocument.Parse(row.Remaining)+document.BattleStartOffsetMs;
        if(offset<0) throw new ArgumentException("该行在本轮起点之前，无法标记。");
        ShiftBattleOrigin(offset,row);
        status.Text=$"已标记开战零点，前置操作时长 {AxisDocument.Format(offset)}。录制的前置重开将在每轮自动重放。";
    }
    private void ShiftBattleOrigin(long offset,AxisEvent? marker)
    {
        long shift=offset-document.BattleStartOffsetMs;
        foreach(var e in document.Events)
        {
            e.Remaining=AxisDocument.Format(AxisDocument.Parse(e.Remaining)+shift);
            e.IsBattleStart=ReferenceEquals(e,marker);
        }
        if(document.EndMode==EndCheckMode.Remaining) document.End=AxisDocument.Format(AxisDocument.Parse(document.End)+shift);
        document.BattleStartOffsetMs=offset;
        end.Text=document.End;
        grid.Refresh();

    }
}


