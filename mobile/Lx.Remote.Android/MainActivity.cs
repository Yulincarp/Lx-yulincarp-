using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace Lx.Remote.Android;

[Activity(
    Label = "Lx 手机遥控",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : Activity
{
    private WebView? _web;
    private EditText? _address;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        root.SetBackgroundColor(Color.ParseColor("#0f1115"));

        var bar = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
        };
        bar.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        bar.SetBackgroundColor(Color.ParseColor("#171a21"));

        _address = new EditText(this)
        {
            Hint = "http://电脑IP:47821/?token=...",
            TextSize = 12,
        };
        _address.SetSingleLine(true);
        _address.SetTextColor(Color.ParseColor("#e8edf5"));
        _address.SetHintTextColor(Color.ParseColor("#8d98a8"));
        _address.SetBackgroundColor(Color.ParseColor("#0c0e12"));

        var connect = new Button(this) { Text = "连接" };
        connect.Click += (_, _) => Connect();

        bar.AddView(_address, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        bar.AddView(connect, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        _web = new WebView(this);
        _web.SetBackgroundColor(Color.ParseColor("#0f1115"));
        _web.Settings.JavaScriptEnabled = true;
        _web.Settings.DomStorageEnabled = true;
        _web.Settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        _web.SetWebViewClient(new WebViewClient());
        _web.SetWebChromeClient(new WebChromeClient());

        root.AddView(bar, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        root.AddView(_web, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        SetContentView(root);

        string? saved = GetSharedPreferences("lx_remote", FileCreationMode.Private)
            ?.GetString("url", null);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            _address.Text = saved;
            _web.LoadUrl(saved);
        }
    }

    private void Connect()
    {
        string? url = _address?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            Toast.MakeText(this, "请先粘贴电脑上显示的地址", ToastLength.Short)?.Show();
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
            _address!.Text = url;
        }

        GetSharedPreferences("lx_remote", FileCreationMode.Private)
            ?.Edit()?.PutString("url", url)?.Apply();
        _web?.LoadUrl(url);
    }

    public override void OnBackPressed()
    {
        if (_web?.CanGoBack() == true)
        {
            _web.GoBack();
            return;
        }

        base.OnBackPressed();
    }

    private int Dp(int value) =>
        (int)(value * (Resources?.DisplayMetrics?.Density ?? 1f));
}
