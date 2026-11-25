namespace BAAxis;

internal sealed partial class MainForm
{
    private Control BuildAxisTabs()
    {
        var panel=new Panel { Dock=DockStyle.Fill };
        var toolbar=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,BackColor=Color.White };
        void Add(string text,Action action)
        {
            var button=new AcademyButton { Text=text,Width=145,Height=36,Margin=new Padding(4) };
            button.Click+=(_,_)=>Guard(action);toolbar.Controls.Add(button);
        }
        Add("标记开战零点",MarkBattleStart);
        Add("清除开战零点",()=> {Idle();ReadFields();ShiftBattleOrigin(0,null);status.Text="已清除开战零点，时间恢复为本轮开始时计时。";});
        panel.Controls.Add(grid);panel.Controls.Add(toolbar);
        grid.CellFormatting+=(_,e)=>
        {
            if(e.RowIndex<0 || EventAt(e.RowIndex) is not AxisEvent row) return;
            if((grid.Columns[e.ColumnIndex].Name is "X" or "Y") && row.Action is ActionKind.KeyDown or ActionKind.KeyUp) {e.Value="";e.FormattingApplied=true;}
        };
        return panel;
    }
    private void SetEditorsReadOnly(bool value)
    {
        grid.ReadOnly=true;
        total.Enabled=!value;count.Enabled=!value;endMode.Enabled=!value;
        end.Enabled=!value && endMode.SelectedIndex==0;
        endWait.Enabled=!value && endMode.SelectedIndex==1;
    }
}
