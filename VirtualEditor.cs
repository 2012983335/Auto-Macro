namespace BAAxis;

internal sealed partial class MainForm
{
    private AxisEvent? EventAt(int index) => index>=0 && index<document.Events.Count ? document.Events[index] : null;
    private void InitializeVirtualGrid()
    {
        grid.VirtualMode=true;
        grid.ReadOnly=true;
        grid.AllowUserToAddRows=false;
        grid.AllowUserToDeleteRows=false;
        grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        grid.RowTemplate.Height=38;
        grid.CellValueNeeded+=(_,e)=>
        {
            var row=EventAt(e.RowIndex);
            if(row==null) return;
            e.Value=grid.Columns[e.ColumnIndex].DataPropertyName switch
            {
                "Remaining"=>row.Remaining,"Action"=>row.Action,"Code"=>row.Code,
                "X"=>row.X,"Y"=>row.Y,"Delta"=>row.Delta,"Extended"=>row.Extended,"IsBattleStart"=>row.IsBattleStart,
                _=>null
            };
        };
    }
}
