Imports System.Windows.Forms

Public Class clsMenus
    Implements LabTech.Interfaces.IMenu

    Private objHost As LabTech.Interfaces.IControlCenter
    Dim F As MainForm

    Public Function CreateMainMenu() As System.Windows.Forms.MenuItem() Implements LabTech.Interfaces.IMenu.CreateMainMenu
        Dim MNUs(0) As System.Windows.Forms.MenuItem
        Dim m As New System.Windows.Forms.MenuItem(PluginName, AddressOf ShowMainForm)
        MNUs(0) = m
        Return MNUs
    End Function

    Public Function CreateToolsMenu() As System.Windows.Forms.MenuItem() Implements LabTech.Interfaces.IMenu.CreateToolsMenu
        Return Nothing
    End Function

    Sub ShowMainForm(sender As Object, e As EventArgs)
        If F IsNot Nothing AndAlso Not F.IsDisposed Then
            F.BringToFront()
        Else
            F = New MainForm(objHost)
            F.Text = PluginName
            F.Show()
        End If
    End Sub

    Public Function CreateViewMenu() As System.Windows.Forms.MenuItem() Implements LabTech.Interfaces.IMenu.CreateViewMenu
        Return Nothing
    End Function

    Public Sub Decommision() Implements LabTech.Interfaces.IMenu.Decommision
        If F IsNot Nothing AndAlso Not F.IsDisposed Then
            F.Dispose()
        End If
        objHost = Nothing
    End Sub

    Public Sub Initialize(Host As LabTech.Interfaces.IControlCenter) Implements LabTech.Interfaces.IMenu.Initialize
        objHost = Host
    End Sub

    Public ReadOnly Property Name As String Implements LabTech.Interfaces.IMenu.Name
        Get
            Return PluginName & " Menu v" & mVersion
        End Get
    End Property

End Class
