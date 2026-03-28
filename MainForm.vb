Imports System.Windows.Forms

''' <summary>
''' Main plugin UI window.
''' STUB — Phase 3 will implement the full 4-tab UI:
'''   Tab 1: Dashboard  — per-location deployment status grid
'''   Tab 2: Locations  — enable/disable locations, trigger manual rotation/deploy
'''   Tab 3: Settings   — edit plugin_triton_msp_settings key-value pairs
'''   Tab 4: Logs       — tail CW Automate log filtered to [TritonAM] entries
''' </summary>
Public Class MainForm
    Inherits Form

    Private ReadOnly _host As LabTech.Interfaces.IControlCenter

    Public Sub New(host As LabTech.Interfaces.IControlCenter)
        _host = host
        InitializeComponent()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = PluginName
        Me.Size = New System.Drawing.Size(960, 640)
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New System.Drawing.Size(640, 480)

        Dim lbl As New Label()
        lbl.Text = PluginName & " v" & mVersion & vbCrLf & vbCrLf &
                   "UI under construction (Phase 3)" & vbCrLf & vbCrLf &
                   "The core engine is active:" & vbCrLf &
                   "  - Password rotation (iSync, daily)" & vbCrLf &
                   "  - New machine detection (iSync2, every 6 min)" & vbCrLf &
                   "  - v2 account migration (iSync2, when enabled)"
        lbl.AutoSize = True
        lbl.Font = New System.Drawing.Font("Segoe UI", 10)
        lbl.Location = New System.Drawing.Point(24, 24)
        Me.Controls.Add(lbl)
    End Sub

End Class
