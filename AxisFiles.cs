using System.Text.Json;

namespace BAAxis;

internal sealed partial class MainForm
{
    private bool fileBusy;
    private async Task LoadAsync(string path)
    {
        Idle();fileBusy=true;SetEditorsReadOnly(true);status.Text="正在读取和校验动作轴…";
        try
        {
            var loaded=await Task.Run(()=>
            {
                using var stream=File.OpenRead(path);
                var value=JsonSerializer.Deserialize<AxisDocument>(stream,AxisDocument.JsonOptions)??throw new ArgumentException("动作轴为空。");
                value.MigrateSkills();value.Compile();return value;
            });
            document=loaded;filePath=path;Bind();status.Text=$"已打开 {document.Events.Count:N0} 个事件";
        }
        finally {fileBusy=false;SetEditorsReadOnly(false);}
    }
    private async Task SaveAsync()
    {
        Idle();ReadFields();
        if(filePath==null)
        {
            using var dialog=new SaveFileDialog { Filter="动作轴 (*.json)|*.json",DefaultExt="json" };
            if(dialog.ShowDialog()!=DialogResult.OK) return;
            filePath=dialog.FileName;
        }
        string path=filePath;
        fileBusy=true;SetEditorsReadOnly(true);status.Text="正在校验并保存动作轴…";
        try
        {
            var snapshot=document.Snapshot();
            await Task.Run(()=>
            {
                snapshot.Compile();
                string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using(var stream=File.Create(temporary)) JsonSerializer.Serialize(stream,snapshot,AxisDocument.JsonOptions);
                    File.Move(temporary,path,true);
                }
                finally {if(File.Exists(temporary)) File.Delete(temporary);}
            });
            status.Text=$"已保存 {snapshot.Events.Count:N0} 个事件";
        }
        finally {fileBusy=false;SetEditorsReadOnly(false);}
    }
}
