Imports System.Text
Imports System.IO
Imports System.Xml
Imports System.Xml.Serialization
Imports LabTech.Interfaces
Imports System.Text.RegularExpressions
Imports System.Reflection
Imports System.Windows.Forms
Imports System.Drawing

Module Globals
    Public Const mVersion As Double = 3.0
    Public Const mAuthor As String = "Triton Technologies"
    Public Const PluginName As String = "Triton Account Manager"

    ' Managed account identity
    Public Const AccountName As String = "TritonTech"
    Public Const AccountFullName As String = "Triton Technologies Support"
    Public Const AccountComment As String = "Managed support account"

    ' Local config path components
    Public Const ConfigDir As String = "Triton\MSPAccounts"
    Public Const ConfigFileName As String = "config.xml"

    ' Database table names
    Public Const TblSettings As String = "plugin_triton_msp_settings"
    Public Const TblDeployments As String = "plugin_triton_msp_deployments"

    ' Default rotation interval (days)
    Public Const DefaultRotationDays As Integer = 90
End Module
