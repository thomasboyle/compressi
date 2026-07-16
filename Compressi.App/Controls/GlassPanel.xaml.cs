using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;



namespace Compressi_App.Controls;



public sealed partial class GlassPanel : UserControl

{

    public static readonly DependencyProperty PanelContentProperty =

        DependencyProperty.Register(

            nameof(PanelContent),

            typeof(object),

            typeof(GlassPanel),

            new PropertyMetadata(null));



    public object? PanelContent

    {

        get => GetValue(PanelContentProperty);

        set => SetValue(PanelContentProperty, value);

    }



    public GlassPanel()

    {

        InitializeComponent();

    }

}

