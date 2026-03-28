Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' Handles password generation and CW Automate password vault integration.
''' Passwords are stored in the native CW Automate passwords table via f_CWAESEncrypt,
''' so they appear in the standard Passwords tab and are keyed to this server's serverId.
''' </summary>
Public Module PasswordManager

    ' Character pools — ambiguous characters (0, O, l, 1, I) intentionally excluded
    Private Const UpperChars As String = "ABCDEFGHJKLMNPQRSTUVWXYZ"
    Private Const LowerChars As String = "abcdefghjkmnpqrstuvwxyz"
    Private Const DigitChars As String = "23456789"
    Private Const SpecialChars As String = "!@#$%^&*()-_=+"

    ''' <summary>
    ''' Generates a cryptographically secure random password.
    ''' Reads length and composition requirements from plugin_triton_msp_settings.
    ''' Falls back to safe defaults if settings are unavailable.
    ''' </summary>
    Public Function GeneratePassword(host As LabTech.Interfaces.IControlCenter) As String
        Dim length As Integer = 20
        Dim minUpper As Integer = 2
        Dim minLower As Integer = 2
        Dim minDigit As Integer = 2
        Dim minSpecial As Integer = 2

        Try
            Dim ds As DataSet = host.GetDataSet(
                "SELECT SettingKey, SettingValue FROM `" & TblSettings & "` " &
                "WHERE SettingKey IN ('password_length','password_upper','password_lower','password_digit','password_special')")
            If ds IsNot Nothing AndAlso ds.Tables.Count > 0 Then
                For Each row As DataRow In ds.Tables(0).Rows
                    Select Case row("SettingKey").ToString()
                        Case "password_length" : length = CInt(row("SettingValue"))
                        Case "password_upper" : minUpper = CInt(row("SettingValue"))
                        Case "password_lower" : minLower = CInt(row("SettingValue"))
                        Case "password_digit" : minDigit = CInt(row("SettingValue"))
                        Case "password_special" : minSpecial = CInt(row("SettingValue"))
                    End Select
                Next
            End If
        Catch ex As Exception
            Logger.Warn(host, "PasswordManager.GeneratePassword: Could not read settings, using defaults. " & ex.Message)
        End Try

        Return BuildPassword(length, minUpper, minLower, minDigit, minSpecial)
    End Function

    Private Function BuildPassword(length As Integer, minUpper As Integer, minLower As Integer,
                                   minDigit As Integer, minSpecial As Integer) As String
        Dim chars As New List(Of Char)
        chars.AddRange(GetRandomChars(UpperChars, minUpper))
        chars.AddRange(GetRandomChars(LowerChars, minLower))
        chars.AddRange(GetRandomChars(DigitChars, minDigit))
        chars.AddRange(GetRandomChars(SpecialChars, minSpecial))

        Dim remaining As Integer = length - chars.Count
        If remaining > 0 Then
            Dim allChars As String = UpperChars & LowerChars & DigitChars & SpecialChars
            chars.AddRange(GetRandomChars(allChars, remaining))
        End If

        Shuffle(chars)
        Return New String(chars.ToArray())
    End Function

    Private Function GetRandomChars(pool As String, count As Integer) As List(Of Char)
        Dim result As New List(Of Char)
        Using rng As New RNGCryptoServiceProvider()
            Dim buf(3) As Byte
            For i As Integer = 1 To count
                rng.GetBytes(buf)
                Dim idx As Integer = CInt(Math.Abs(BitConverter.ToInt32(buf, 0))) Mod pool.Length
                result.Add(pool(idx))
            Next
        End Using
        Return result
    End Function

    Private Sub Shuffle(chars As List(Of Char))
        Using rng As New RNGCryptoServiceProvider()
            Dim buf(3) As Byte
            For i As Integer = chars.Count - 1 To 1 Step -1
                rng.GetBytes(buf)
                Dim j As Integer = CInt(Math.Abs(BitConverter.ToInt32(buf, 0))) Mod (i + 1)
                Dim tmp As Char = chars(i)
                chars(i) = chars(j)
                chars(j) = tmp
            Next
        End Using
    End Sub

    ''' <summary>
    ''' Updates the CW Automate native passwords table using f_CWAESEncrypt.
    ''' The updated entry will appear in the CW Passwords tab for the location.
    ''' encryptLevel 0 uses the server's own serverId as the AES key.
    ''' </summary>
    Public Sub UpdateCWPassword(host As LabTech.Interfaces.IControlCenter, passwordID As Integer, newPassword As String)
        Dim safePass As String = newPassword.Replace("'", "''")
        host.SetSQL(
            "UPDATE passwords " &
            "SET Password = f_CWAESEncrypt(0, '" & safePass & "'), " &
            "Last_User = 'TritonAM_AutoRotate', " &
            "Last_Date = NOW(), " &
            "ExpireDate = DATE_ADD(NOW(), INTERVAL " & DefaultRotationDays & " DAY) " &
            "WHERE PasswordID = " & passwordID)
    End Sub

    ''' <summary>
    ''' Reads the plaintext password back from the CW passwords table via f_CWAESDecrypt.
    ''' f_CWAESDecrypt confirmed present on this server (encryptLevel TBD — verify with
    ''' SELECT CAST(f_CWAESDecrypt(0, Password) AS CHAR) on a known password row).
    ''' Returns Nothing if unavailable.
    ''' </summary>
    Public Function ReadCWPassword(host As LabTech.Interfaces.IControlCenter, passwordID As Integer) As String
        Try
            Dim result As String = host.GetSQL(
                "SELECT CAST(f_CWAESDecrypt(0, Password) AS CHAR) FROM passwords WHERE PasswordID = " & passwordID)
            If result Is Nothing OrElse result = "-9999" Then Return Nothing
            Return result
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Creates a new row in the CW native passwords table for a location.
    ''' Returns the new PasswordID, or 0 on failure.
    ''' Call this when first deploying to a location that has no password entry yet.
    ''' Column name confirmed from DESCRIBE passwords: UserName (not Username).
    ''' </summary>
    Public Function CreateCWPassword(host As LabTech.Interfaces.IControlCenter, locationID As Integer,
                                     initialPassword As String) As Integer
        Dim safePass As String = initialPassword.Replace("'", "''")
        Dim locationName As String = host.GetSQL("SELECT Name FROM locations WHERE LocationID = " & locationID)
        If locationName Is Nothing OrElse locationName = "-9999" Then locationName = "Location " & locationID
        Dim safeName As String = locationName.Replace("'", "''")

        host.SetSQL(
            "INSERT INTO passwords (Title, UserName, Password, Last_User, Last_Date, ExpireDate, LocationID, ClientID) " &
            "SELECT '" & safeName & " - TritonTech', '" & AccountName & "', " &
            "f_CWAESEncrypt(0, '" & safePass & "'), " &
            "'TritonAM_Deploy', NOW(), DATE_ADD(NOW(), INTERVAL " & DefaultRotationDays & " DAY), " &
            locationID & ", ClientID FROM locations WHERE LocationID = " & locationID)

        Dim newID As String = host.GetSQL("SELECT LAST_INSERT_ID()")
        If newID Is Nothing OrElse newID = "-9999" Then Return 0
        Return CInt(newID)
    End Function

End Module
