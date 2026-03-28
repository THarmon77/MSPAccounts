Imports System.IO
Imports System.Xml

''' <summary>
''' Reads and writes the local workstation config file at
''' %SystemDrive%\Triton\MSPAccounts\config.xml.
''' This file stores workstation-specific settings; server-side settings
''' (rotation interval, account name, password policy, etc.) live in plugin_triton_msp_settings.
''' </summary>
Public Module Config

    Private ReadOnly _configPath As String =
        Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") & "\" & ConfigDir, ConfigFileName)

    Public Function GetConfigPath() As String
        Return _configPath
    End Function

    Public Function EnsureConfigDirectory() As Boolean
        Try
            Dim dir As String = Path.GetDirectoryName(_configPath)
            If Not Directory.Exists(dir) Then
                Directory.CreateDirectory(dir)
                Dim di As New DirectoryInfo(dir)
                di.Attributes = di.Attributes Or FileAttributes.Hidden
            End If
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    Public Function ReadSetting(key As String, defaultValue As String) As String
        Try
            If Not File.Exists(_configPath) Then Return defaultValue
            Dim doc As New XmlDocument()
            doc.Load(_configPath)
            Dim node As XmlNode = doc.SelectSingleNode("/config/" & key)
            If node IsNot Nothing Then Return node.InnerText
        Catch ex As Exception
            ' Config file is optional; read failure is non-fatal
        End Try
        Return defaultValue
    End Function

    Public Sub WriteSetting(key As String, value As String)
        Try
            EnsureConfigDirectory()
            Dim doc As New XmlDocument()
            If File.Exists(_configPath) Then
                doc.Load(_configPath)
            Else
                doc.LoadXml("<?xml version=""1.0"" encoding=""utf-8""?><config/>")
            End If
            Dim root As XmlNode = doc.SelectSingleNode("/config")
            Dim node As XmlNode = root.SelectSingleNode(key)
            If node Is Nothing Then
                node = doc.CreateElement(key)
                root.AppendChild(node)
            End If
            node.InnerText = value
            doc.Save(_configPath)
        Catch ex As Exception
            ' Config write failure is non-fatal
        End Try
    End Sub

End Module
