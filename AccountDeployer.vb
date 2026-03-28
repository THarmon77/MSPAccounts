Imports System.Threading

''' <summary>
''' Deploys, rotates, and removes the TritonTech managed account on target workstations
''' via CW Automate SendCommand (CmdID=2 shell commands).
''' All commands poll for completion with a configurable timeout.
''' </summary>
Public Module AccountDeployer

    ' CmdID 2 = shell command execution via CW Automate agent
    Private Const CmdShell As Integer = 2
    ' Maximum seconds to wait for a command to complete before throwing TimeoutException
    Private Const CmdTimeoutSeconds As Integer = 120
    ' Polling interval in milliseconds
    Private Const PollIntervalMs As Integer = 3000

    ''' <summary>
    ''' Deploys TritonTech account on a single machine:
    '''   1. Creates local user with net user /add
    '''   2. Adds to local Administrators group
    '''   3. Hides account from the Windows login screen via registry
    ''' </summary>
    Public Sub DeployAccount(host As LabTech.Interfaces.IControlCenter, computerID As Integer, password As String)
        Dim safePass As String = EscapeShellArg(password)

        RunCommand(host, computerID,
            "net user " & AccountName & " """ & safePass & """" &
            " /add" &
            " /fullname:""" & AccountFullName & """" &
            " /comment:""" & AccountComment & """" &
            " /passwordchg:no /expires:never")

        RunCommand(host, computerID,
            "net localgroup Administrators " & AccountName & " /add")

        RunCommand(host, computerID,
            "reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList""" &
            " /v " & AccountName & " /t REG_DWORD /d 0 /f")

        Logger.Info(host, "AccountDeployer: Deployed " & AccountName & " on ComputerID=" & computerID)
    End Sub

    ''' <summary>
    ''' Rotates (changes) the password on an already-deployed TritonTech account.
    ''' </summary>
    Public Sub RotatePassword(host As LabTech.Interfaces.IControlCenter, computerID As Integer, newPassword As String)
        Dim safePass As String = EscapeShellArg(newPassword)
        RunCommand(host, computerID, "net user " & AccountName & " """ & safePass & """")
        Logger.Info(host, "AccountDeployer: Rotated password on ComputerID=" & computerID)
    End Sub

    ''' <summary>
    ''' Queues password rotation on all currently-deployed machines in a location.
    ''' Only targets machines that are online (last contact within target_last_contact_hours)
    ''' and already have the TritonTech account (per computers.UserAccounts).
    ''' Updates DeployedCount and ErrorCount in plugin_triton_msp_deployments.
    ''' </summary>
    Public Sub RotatePasswordAllMachines(host As LabTech.Interfaces.IControlCenter, locationID As Integer, newPassword As String)
        Dim targetHours As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_last_contact_hours'")
        If targetHours Is Nothing OrElse targetHours = "-9999" Then targetHours = "1"

        Dim osFilter As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_os_filter'")
        If osFilter Is Nothing OrElse osFilter = "-9999" Then osFilter = "NOT LIKE '%Server%'"

        Dim ds As DataSet = host.GetDataSet(
            "SELECT c.ComputerID, c.ComputerName FROM computers c " &
            "WHERE c.LocationID = " & locationID & " " &
            "AND c.OS " & osFilter & " " &
            "AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < " & targetHours & " " &
            "AND c.UserAccounts LIKE '%:" & AccountName & ":%'")

        If ds Is Nothing OrElse ds.Tables.Count = 0 Then Return

        Dim deployed As Integer = 0
        Dim errors As Integer = 0
        For Each row As DataRow In ds.Tables(0).Rows
            Dim computerID As Integer = CInt(row("ComputerID"))
            Try
                RotatePassword(host, computerID, newPassword)
                deployed += 1
            Catch ex As Exception
                errors += 1
                Logger.Err(host, "AccountDeployer.RotatePasswordAllMachines: Error on ComputerID=" & computerID & ": " & ex.Message)
            End Try
        Next

        host.SetSQL(
            "UPDATE `" & TblDeployments & "` " &
            "SET DeployedCount = " & deployed & ", ErrorCount = ErrorCount + " & errors & ", UpdatedAt = NOW() " &
            "WHERE LocationID = " & locationID)
    End Sub

    ''' <summary>
    ''' Removes the TritonTech account from a machine.
    ''' </summary>
    Public Sub RemoveAccount(host As LabTech.Interfaces.IControlCenter, computerID As Integer)
        RunCommand(host, computerID, "net user " & AccountName & " /delete")
        Logger.Info(host, "AccountDeployer: Removed " & AccountName & " from ComputerID=" & computerID)
    End Sub

    ''' <summary>
    ''' Sends a shell command via CW Automate and polls until completion or timeout.
    ''' Throws TimeoutException if command does not complete within CmdTimeoutSeconds.
    ''' Throws ApplicationException if command status is Error (4).
    ''' </summary>
    Public Sub RunCommand(host As LabTech.Interfaces.IControlCenter, computerID As Integer, command As String)
        Dim cmdID As Integer = host.SendCommand(computerID, CmdShell, "cmd!!!" & command)
        If cmdID <= 0 Then
            Throw New ApplicationException("SendCommand returned invalid cmdID=" & cmdID & " for ComputerID=" & computerID)
        End If

        Dim maxAttempts As Integer = (CmdTimeoutSeconds * 1000) \ PollIntervalMs
        Dim attempts As Integer = 0
        Dim status As Integer = 0

        Do
            Thread.Sleep(PollIntervalMs)
            attempts += 1
            If attempts >= maxAttempts Then
                Throw New TimeoutException("Command timed out after " & CmdTimeoutSeconds & "s on ComputerID=" & computerID)
            End If
            Dim statusStr As String = host.GetSQL("SELECT Status FROM commands WHERE CmdID=" & cmdID)
            If statusStr Is Nothing OrElse statusStr = "-9999" Then Continue Do
            status = CInt(statusStr)
        Loop While status < 3

        If status = 4 Then
            Dim output As String = host.GetSQL("SELECT Output FROM commands WHERE CmdID=" & cmdID)
            If output Is Nothing OrElse output = "-9999" Then output = "(no output)"
            Throw New ApplicationException("Command error on ComputerID=" & computerID & ". Output: " & output)
        End If
    End Sub

    ''' <summary>
    ''' Escapes a password for safe use in a Windows cmd.exe command argument.
    ''' </summary>
    Private Function EscapeShellArg(value As String) As String
        Return value.Replace("%", "%%").Replace("""", """""""")
    End Function

End Module
