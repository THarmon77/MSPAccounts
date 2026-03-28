Public Class clsSync
    Implements LabTech.Interfaces.ISync
    Private m_Host As LabTech.Interfaces.IControlCenter

    Public Sub Initialize(ByVal Host As LabTech.Interfaces.IControlCenter) Implements LabTech.Interfaces.ISync.Initialize
        m_Host = Host
    End Sub

    Public Sub Decommision() Implements LabTech.Interfaces.ISync.Decommision
        m_Host = Nothing
    End Sub

    Public ReadOnly Property Name As String Implements LabTech.Interfaces.ISync.Name
        Get
            Return PluginName & "_ISync_v" & mVersion
        End Get
    End Property

    ''' <summary>
    ''' Called once per day at midnight by CW Automate.
    ''' Checks plugin_triton_msp_deployments for locations where RotationDue <= NOW()
    ''' and triggers password rotation on all machines in each due location.
    ''' </summary>
    Public Sub Syncronize() Implements LabTech.Interfaces.ISync.Syncronize
        Try
            Logger.Info(m_Host, "iSync: Daily rotation check starting")

            Dim ds As DataSet = m_Host.GetDataSet(
                "SELECT d.LocationID, d.LocationName, d.PasswordID " &
                "FROM `" & TblDeployments & "` d " &
                "WHERE d.Status = 1 " &
                "AND d.PasswordID IS NOT NULL " &
                "AND d.RotationDue IS NOT NULL " &
                "AND d.RotationDue <= NOW()")

            If ds Is Nothing OrElse ds.Tables.Count = 0 OrElse ds.Tables(0).Rows.Count = 0 Then
                Logger.Info(m_Host, "iSync: No locations due for rotation")
                Return
            End If

            For Each row As DataRow In ds.Tables(0).Rows
                Dim locationID As Integer = CInt(row("LocationID"))
                Dim locationName As String = row("LocationName").ToString()
                Dim passwordID As Integer = CInt(row("PasswordID"))
                Try
                    Dim newPassword As String = PasswordManager.GeneratePassword(m_Host)
                    PasswordManager.UpdateCWPassword(m_Host, passwordID, newPassword)
                    AccountDeployer.RotatePasswordAllMachines(m_Host, locationID, newPassword)
                    m_Host.SetSQL(
                        "UPDATE `" & TblDeployments & "` " &
                        "SET LastRotated = NOW(), " &
                        "RotationDue = DATE_ADD(NOW(), INTERVAL " & DefaultRotationDays & " DAY), " &
                        "UpdatedAt = NOW() " &
                        "WHERE LocationID = " & locationID)
                    Logger.Info(m_Host, "iSync: Rotation complete for " & locationName & " (LocationID=" & locationID & ")")
                Catch ex As Exception
                    Dim msg As String = ex.Message.Substring(0, Math.Min(490, ex.Message.Length))
                    Logger.Err(m_Host, "iSync: Rotation failed for " & locationName & " (LocationID=" & locationID & "): " & ex.Message)
                    m_Host.SetSQL(
                        "UPDATE `" & TblDeployments & "` " &
                        "SET ErrorCount = ErrorCount + 1, " &
                        "LastError = '" & msg.Replace("'", "''") & "', " &
                        "UpdatedAt = NOW() " &
                        "WHERE LocationID = " & locationID)
                End Try
            Next

            Logger.Info(m_Host, "iSync: Daily rotation check complete")
        Catch ex As Exception
            Logger.Err(m_Host, "iSync: Syncronize fatal error: " & ex.Message)
        End Try
    End Sub

End Class
