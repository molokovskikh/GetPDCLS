using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.AccessControl;
using System.Security.Principal;
using ADOX;

using SharpCompress.Archive;
using SharpCompress.Archive.GZip;
using SharpCompress.Archive.Rar;
using SharpCompress.Common;
using SharpCompress.Compressor;
using SharpCompress.Compressor.Deflate;

namespace MarkTrade
{
    public class Converter
    {
        string _dir = null;
        public Converter(string dir)
        {
            this._dir = dir;
        }

        public void Convert()
        {            
            //Получим файл Excel c http://grls.rosminzdrav.ru/pricelims.aspx
            //Для этого сначала запросим страницу и найдем на ней нужный файл для скачивания
            string urlFile = FindFileImport();
            if (!string.IsNullOrEmpty(urlFile))
            {
                
                byte [] fileBytes = GetRawByUrl(new Uri(urlFile));
                byte[] xlsBytes = fileBytes;
                if (fileBytes != null && fileBytes.Length > 1)
                {
                    if (fileBytes[0] == 0x50 && fileBytes[1] == 0x4B)//Это архив?
                    {
                        xlsBytes = Unzip(fileBytes);
                    }
                }

                if (xlsBytes != null)
                {
                   DataTable dataImport = GetDataTable(xlsBytes);
                   CreateMde2(dataImport);
                   Rar();
                }
            }

          
        }

        private string FindFileImport()
        {
            byte [] pageBytes = GetRawByUrl(new Uri("http://grls.rosminzdrav.ru/pricelims.aspx"));
            if (pageBytes != null)
            {
                List<string> fileUrl = new List<string>();
                string content = Encoding.Default.GetString(pageBytes);
                foreach (Match m in Regex.Matches(content, @"\<a\s+href\=[\'\""](?<uri>GetLimPrice\.aspx\?FileGUID\=[^\'\""]*)[\'\""]\>", RegexOptions.IgnoreCase))
                {
                    string uri = m.Groups["uri"].Value;
                    if(!string.IsNullOrEmpty(uri))
                    {
                        fileUrl.Add(string.Format("http://grls.rosminzdrav.ru{0}{1}", uri.StartsWith("/")?string.Empty:"/", uri));
                    }
                }
                if (fileUrl.Count > 0)
                    return fileUrl.First();
            }
            return null;
        }


        private byte[] Unzip(byte [] zip)
        {
            byte[] res = null;
            using (Stream stream = new MemoryStream(zip))
            using (var archive = GZipArchive.Open(stream, Options.KeepStreamsOpen))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        using (Stream src = entry.OpenEntryStream())
                        {

                            using (MemoryStream dst = new MemoryStream())
                            {
                                int reading = 0;
                                byte [] buf = new byte[1024];
                                while ((reading=src.Read(buf,0,buf.Length))>0)
                                {
                                    dst.Write(buf,0,reading);
                                }                                
                                res = dst.ToArray();
                            }
                        }
                    }
                }
            }
            return res;
        }


        private void Rar()
        {            
            string file_compress = Path.Combine(_dir, "reestrx.rar");
            string file_uncompress = Path.Combine(_dir, "reestrx.mde");
            string cmd_args = string.Format(@"a -ep1 ""{0}"" ""{1}""", file_compress, file_uncompress);
            System.Diagnostics.Process process =  new System.Diagnostics.Process();
            
            if (File.Exists(file_compress))
                File.Delete(file_compress);

            process.StartInfo = new System.Diagnostics.ProcessStartInfo ( Path.Combine(_dir, @"bin\Rar.exe"),cmd_args);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            if(process.Start())                
                process.WaitForExit();

            if (File.Exists(file_compress))
            {                                
                if (File.Exists(file_uncompress))
                    File.Delete(file_uncompress);
            }
        }

        DataTable CreateDataTable()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("MNN");
            dt.Columns.Add("TNAME");
            dt.Columns.Add("LEKFORM");
            dt.Columns.Add("MANUFACTURE");
            dt.Columns.Add("NPACK");
            dt.Columns.Add("PRICE");
            dt.Columns.Add("PRICEFIRSTPACK");
            dt.Columns.Add("NRU");
            dt.Columns.Add("DTREG");
            dt.Columns.Add("EAN");
            return dt;
        }


        DataTable GetDataTable(byte[] xlsBytes)
        {
            DataTable resultTable = null;
            string connXls = GetConnectionStringExcel(xlsBytes);
            using (OleDbConnection connection = new OleDbConnection(connXls))
            {
                if (connection.State != ConnectionState.Open)
                    connection.Open();
                DataTable sheets = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null);
                string[] excelSheets = new String[sheets.Rows.Count];
                int i = 0;
                foreach (DataRow row in sheets.Rows)
                {
                    excelSheets[i] = row["TABLE_NAME"].ToString();
                    i++;
                }

                foreach (var excelSheet in excelSheets)
                {
                    using (OleDbCommand cmd = new OleDbCommand(string.Format("select * from [{0}]", excelSheet), connection))
                    {
                        resultTable = resultTable ?? CreateDataTable();
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            int cnt = 0;
                            while (reader.Read())
                            {
                                DataRow row = null;
                                if (cnt>1&&resultTable.Columns.Count <= reader.FieldCount)
                                {
                                    for (int f = 0; f < resultTable.Columns.Count; f++)
                                    {
                                        if (!reader.IsDBNull(f))
                                        {
                                            row = row??resultTable.NewRow();
                                            row[f] = reader.GetValue(f);
                                        }
                                    }
                                    if (row != null)                                    
                                        resultTable.Rows.Add(row);                                    
                                }
                                cnt++;
                            }
                        }                        
                    }
                }
            }

	    string tmp_file = Path.Combine(_dir, "lp.xls");
            if (File.Exists(tmp_file))
                File.Delete(tmp_file);
            
            return resultTable;
        }


        private string GetConnectionStringExcel(byte [] raw)
        {
            string tmp_file = Path.Combine(_dir, "lp.xls");
            if (File.Exists(tmp_file))
                File.Delete(tmp_file);

            File.WriteAllBytes(tmp_file,raw);

            return string.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0};Extended Properties=Excel 8.0", tmp_file);
        }


private bool GrantAccess(string fullPath)
{
    DirectoryInfo dInfo = new DirectoryInfo(fullPath);
    DirectorySecurity dSecurity = dInfo.GetAccessControl();
    dSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
    dInfo.SetAccessControl(dSecurity);
    return true;
}

        private void CreateMde2(DataTable dataImport)
        {
            string dtUpdate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss").Replace(".", "/");           
            string mde_template = Path.Combine(_dir, @"Bin\template.mde");
            


            if (File.Exists(mde_template))
            {                
                string mde_file = Path.Combine(_dir, "reestrx.mde");
                //mde_file = @"d:\DZHosts\LocalUser\lekfarm\Protected.lekfarm.somee.com\reestrx.mde";
                File.Copy(mde_template, mde_file, true);
		GrantAccess(mde_file);
                

                using (OleDbConnection connection = new OleDbConnection(string.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0};Persist Security Info = false;Jet OLEDB:Engine Type=4", mde_file)))
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    for (int i = 0; i < dataImport.Rows.Count; i++)
                    {
                        string query = string.Format(System.Globalization.CultureInfo.InvariantCulture, @"
INSERT INTO {0} (N, Наименование, Форма, [В упаковке], Производитель, [Штрих-код], Регистрация, Цена, [Цена руб], [Код производителя], МНН, [Цена УЕ], УЕ, [Действует?], [Дата обновления], [Снято с регистрации], [Дата регистрации], D)
VALUES (NULL,'{2}','{3}','{5}','{4}',{10},'{8} от {9}','{6:0,00} Руб',{6:0.00},0,'{1}',{6:0.00},'Руб',0,#{11}#,0,NULL,1)",
                            "Госреестр",
                            dataImport.Rows[i][0],
                            dataImport.Rows[i][1].ToString().Replace("'", "''"),
                            dataImport.Rows[i][2],
                            dataImport.Rows[i][3].ToString().Replace("'", "''"),
                            dataImport.Rows[i][4] != null && string.IsNullOrEmpty(dataImport.Rows[i][4].ToString()) ? "0" : dataImport.Rows[i][4].ToString(),
                            dataImport.Rows[i][5].ToString().Replace(",", "."),
                            dataImport.Rows[i][6],
                            dataImport.Rows[i][7],
                            dataImport.Rows[i][8].ToString().Replace("\n", string.Empty),
                            dataImport.Rows[i][9] != null && string.IsNullOrEmpty(dataImport.Rows[i][9].ToString()) ? "0" : dataImport.Rows[i][9].ToString(),
                            dtUpdate
                            );
                        try
                        {
                            using (OleDbCommand cmd = new OleDbCommand(query
                          , connection))
                            {                                
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch (Exception exc)
                        {
                            throw new Exception(mde_file, exc);
                        }
                    }
                }
            }
        }



        private byte[] CreateMde(DataTable dataImport)
        {

            string dtUpdate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss").Replace(".","/");


            bool is_good = false;
            string mde_template = Path.Combine(_dir, @"Bin\template.mde");
            string mdb_file = null;


            if (File.Exists(mde_template))
            {
                is_good = true;
                mdb_file = Path.Combine(_dir, "reestrx.mde");
                File.Copy(mde_template, mdb_file,true);
		GrantAccess(mdb_file);
            }
            else
            {               
                mdb_file = Path.Combine(_dir, "reestrx.mdb");
                
                if (File.Exists(mdb_file))
                    File.Delete(mdb_file);

                ADOX.Catalog cat = new ADOX.Catalog();
                ADOX.Table table = new ADOX.Table();
            
                table.Name = "Госреестр";
                table.Columns.Append("N", DataTypeEnum.adVarWChar);
                table.Columns.Append("Наименование");
                table.Columns.Append("Форма");
                table.Columns.Append("В упаковке");
                table.Columns.Append("Производитель");
                table.Columns.Append("Штрих-код", DataTypeEnum.adCurrency, 19);
                table.Columns.Append("Регистрация");
                table.Columns.Append("Цена");
                table.Columns.Append("Цена руб", DataTypeEnum.adCurrency, 19);
                table.Columns.Append("Код производителя", DataTypeEnum.adInteger, 10);
                table.Columns.Append("МНН");
                table.Columns.Append("Цена УЕ", DataTypeEnum.adCurrency, 19);
                table.Columns.Append("УЕ", DataTypeEnum.adVarWChar, 10);
                table.Columns.Append("Id", DataTypeEnum.adInteger, 10);
                table.Columns.Append("Действует?", DataTypeEnum.adBoolean, 2);
                table.Columns.Append("Дата обновления", DataTypeEnum.adDate);
                table.Columns.Append("Снято с регистрации", DataTypeEnum.adBoolean, 2);
                table.Columns.Append("Дата регистрации", DataTypeEnum.adDate);
                table.Columns.Append("D", DataTypeEnum.adBoolean, 2);

                table.Columns["N"].Attributes = ColumnAttributesEnum.adColNullable;
                table.Columns["Дата регистрации"].Attributes = ColumnAttributesEnum.adColNullable;
                table.Columns["Id"].Attributes = ColumnAttributesEnum.adColNullable;

//                table.Columns["Id"].Properties["AutoIncrement"].Value = true;


                try
                {
                    cat.Create("Provider=Microsoft.Jet.OLEDB.4.0;" + "Data Source=" + mdb_file + "; Jet OLEDB:Engine Type=4");
                    cat.Tables.Append(table);

                    //Now Close the database
                    object con = cat.ActiveConnection;
                    if (con != null)
                        con.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, con, null);
                    is_good = true;

                }
                catch (Exception ex)
                {
                }

                table = null;
                cat = null;
            }

            if (is_good)
            {
                
                    using (OleDbConnection connection = new OleDbConnection(string.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0};Jet OLEDB:Engine Type=4", mdb_file)))
                    {
                        if (connection.State != ConnectionState.Open)
                            connection.Open();

                        for (int i = 0; i < dataImport.Rows.Count; i++)
                        {
                            string query = string.Format(System.Globalization.CultureInfo.InvariantCulture, @"
INSERT INTO {0} (N, Наименование, Форма, [В упаковке], Производитель, [Штрих-код], Регистрация, Цена, [Цена руб], [Код производителя], МНН, [Цена УЕ], УЕ, [Действует?], [Дата обновления], [Снято с регистрации], [Дата регистрации], D)
VALUES (NULL,'{2}','{3}','{5}','{4}',{10},'{8} от {9}','{6:0,00} Руб',{6:0.00},0,'{1}',{6:0.00},'Руб',0,#{11}#,0,NULL,1)",
                                "Госреестр",
                                dataImport.Rows[i][0],
                                dataImport.Rows[i][1].ToString().Replace("'","''"),
                                dataImport.Rows[i][2],
                                dataImport.Rows[i][3].ToString().Replace("'","''"),
                                dataImport.Rows[i][4] != null && string.IsNullOrEmpty(dataImport.Rows[i][4].ToString()) ? "0" : dataImport.Rows[i][4].ToString(), 
                                dataImport.Rows[i][5].ToString().Replace(",","."),
                                dataImport.Rows[i][6],
                                dataImport.Rows[i][7],
                                dataImport.Rows[i][8].ToString().Replace("\n", string.Empty),
                                dataImport.Rows[i][9]!=null&&string.IsNullOrEmpty(dataImport.Rows[i][9].ToString())?"0": dataImport.Rows[i][9].ToString(),
                                dtUpdate
                                );
                           try
                           {
                            using (OleDbCommand cmd = new OleDbCommand(query
                          , connection))
                            {
                               // throw new Exception(cmd.CommandText);
                                cmd.ExecuteNonQuery();
                            }
                           }
                           catch (Exception exc)
                           {
                               throw new Exception(mdb_file, exc);
                           }
                        }
                    }
               


                if(!string.IsNullOrEmpty(mdb_file))
                    return MdbToMde(mdb_file);
            }

            if (!string.IsNullOrEmpty(mde_template))
                return File.ReadAllBytes(mde_template);

            return null;
        }


        private byte [] MdbToMde(string mdb_file)
        {
            byte[] result = null;
            if (File.Exists(mdb_file))
            {
                string mde_file = Path.GetFileNameWithoutExtension(mdb_file) + ".mde";
                object objAccess = Activator.CreateInstance(Type.GetTypeFromProgID("Access.Application"));
                objAccess.GetType().InvokeMember("SysCmd", BindingFlags.InvokeMethod, null, objAccess, new object[] { 602, mdb_file, mde_file });
                objAccess.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, objAccess, null);
                objAccess = null;
                if (File.Exists(mde_file))
                {
                    result = File.ReadAllBytes(mde_file);
                    File.Delete(mde_file);
                }
                else
                {
                    if (File.Exists(mdb_file))
                    {
                        result = File.ReadAllBytes(mdb_file);
                        File.Delete(mdb_file);
                    }
                }
            }
            return result;
        }

        private bool IsFile(string filename)
        {
            try
            {
                Path.GetFullPath(filename);
                return true;
            }
            catch (Exception)
            {
                                
            }
            return false;
        }

        /*
        private Stream PrepareDestination(string destination)
        {
            Stream result = null;
            Uri uri = null;
            if (IsFile(destination))
            {
                result = new FileStream(destination,FileMode.Create);
            }
            else 
            if (Uri.TryCreate(destination, UriKind.Absolute, out uri))
            {                               
                if (uri.IsFile)
                {
                    result = new FileStream(destination, FileMode.Create);
                }
                else
                {
                    result = new UpdloadStream(uri);   
                }
            }
            return result;
        }
        */

        byte[] GetRawByFile(string filename)
        {
            byte[] result = null;
            if (File.Exists(filename))
            {
                result = File.ReadAllBytes(filename);
            }
            return result;
        }


        byte[] GetRawByUrl(Uri url)
        {
            byte[] result = null;

            WebRequest request = WebRequest.Create(url);
            using (WebResponse response = request.GetResponse())
            {
                using (MemoryStream dst = new MemoryStream())
                {
                    using (Stream src = response.GetResponseStream())
                    {
                        if (src != null)
                        {
                            int reading = 0;
                            byte[] buf = new byte[1024];
                            while ((reading = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                dst.Write(buf, 0, reading);
                            }              
                        }                        
                    }
                    result = dst.ToArray();
                }
                
            }            
            return result;
        }

    }
}
