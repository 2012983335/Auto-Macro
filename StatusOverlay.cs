namespace BAAxis;

internal sealed class StatusOverlay : Form
{
    private readonly BufferedLabel text = new() { Dock=DockStyle.Fill,ForeColor=Color.White,BackColor=Color.FromArgb(28,48,65),Padding=new Padding(12,8,12,8) };
    public StatusOverlay()
    {
        FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;
        TopMost=true;
        StartPosition=FormStartPosition.Manual;
        Bounds=new Rectangle(20,20,580,60);
        BackColor=text.BackColor;
        Opacity=0.94;
        Font=new Font("Microsoft YaHei UI",10);
        Controls.Add(text);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters=base.CreateParams;parameters.ExStyle|=0x08000000|0x20|0x80;return parameters; }
    }
    public void SetMessage(string message,bool recording=false)
    {
        string value=(recording?"● 正在录制  ":"")+message;
        if(text.Text!=value) text.Text=value;
        text.ForeColor=recording?Color.FromArgb(255,160,160):Color.White;
    }
}
