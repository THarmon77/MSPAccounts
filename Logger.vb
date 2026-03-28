''' <summary>
''' Thin wrapper around IControlCenter.LogMessage providing Info/Warn/Error severity levels.
''' All messages are prefixed with [TritonAM] for easy filtering in the CW Automate log viewer.
''' </summary>
Public Module Logger

    Private Const Prefix As String = "[TritonAM]"

    Public Sub Info(host As LabTech.Interfaces.IControlCenter, message As String)
        If host IsNot Nothing Then
            host.LogMessage(Prefix & " INFO  " & message)
        End If
    End Sub

    Public Sub Warn(host As LabTech.Interfaces.IControlCenter, message As String)
        If host IsNot Nothing Then
            host.LogMessage(Prefix & " WARN  " & message)
        End If
    End Sub

    Public Sub Err(host As LabTech.Interfaces.IControlCenter, message As String)
        If host IsNot Nothing Then
            host.LogMessage(Prefix & " ERROR " & message)
        End If
    End Sub

End Module
