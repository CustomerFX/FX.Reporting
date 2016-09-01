﻿using System;
using System.IO;
using System.Linq;
using System.Data.OleDb;
using log4net;
using Sage.Platform;
using Sage.Platform.Application;
using Sage.Platform.Data;
using Sage.Entity.Interfaces;
using Sage.SalesLogix.DelphiBridge;
using Sage.SalesLogix.Plugins;
using SlxReporting;

namespace FX.Reporting
{
    /* NOTES---
     * 
     * ReportPlugin parameter must be in the format of Family:ReportPluginName
     * 
     * 
     * RecordSelectionFormula must be a valid Crystal record selection formula. Examples:
     * 
     * Example #1:
     * "{OPPORTUNITY.OPPORTUNITYID} = '" + someOpportunityId + "'"
     * 
     * Example #2:
     * "{OPPORTUNITYQUOTE.OPPORTUNITYQUOTEID} = '" + someOpportunityQuoteId + "' AND {OPPORTUNITY_CONTACT.ISPRIMARY} = 'T' AND NOT ({PRODUCT.NAME} = 'Custom Door Panel' OR {PRODUCT.NAME} = 'Custom Drawer Panel')"
     * 
     * 
     * For attaching to a record, it is the responsibility of the code using this assembly to set the references for the attachment record and then save it. For example:
     * 
     * var attachment = AttachReport("System:My Report", "{OPPORTUNITY.OPPORTUNITYID} = '" + someOpportunityId + "'");
     * attachment.OpportunityId = someOpportunityId;
     * attachment.AccountId = someAccountId;
     * attachment.Save();
     * 
     */

    public class ReportGenerator
    {
        public IAttachment AttachReport(string ReportPlugin, string RecordSelectionFormula, string Description = null)
        {
            var reportOutput = ExportReportToPDF(ReportPlugin, RecordSelectionFormula);
            return CreateAttachment(reportOutput, Description);
        }

        public string ExportReportToPDF(string ReportPlugin, string RecordSelectionFormula, string OutputFileName = null, string OutputFilePath = null)
        {
            var log = LogManager.GetLogger(typeof(ReportGenerator));

            var reportPluginParts = ReportPlugin.Split();
            if (reportPluginParts.Length != 2)
                throw new ArgumentException("The report plugin name for the ReportPlugin argument is not valid. The parameter must be in the format of Family:ReportPluginName.");

            if (OutputFileName == null) OutputFileName = string.Format("{0}-{1}.pdf", reportPluginParts[1], Environment.TickCount);
            if (OutputFilePath == null || !Directory.Exists(OutputFilePath)) OutputFilePath = Path.GetTempPath();
            var fileName = Path.Combine(OutputFilePath, SanitizeFileName(OutputFileName));
            log.Debug("File name to output report to: " + fileName);

            var plugin = Plugin.LoadByName(reportPluginParts[0], reportPluginParts[1], PluginType.CrystalReport);
            if (plugin != null)
            {
                var tempRpt = Path.GetTempFileName() + ".rpt";
                using (var stream = new MemoryStream(plugin.Blob.Data))
                {
                    using (var reader = new DelphiBinaryReader(stream))
                    {
                        reader.ReadComponent(true);
                        using (var file = File.OpenWrite(tempRpt))
                        {
                            stream.CopyTo(file);
                        }
                    }
                }

                using (var report = new SlxReport())
                {
                    report.Load(tempRpt);
                    report.UpdateDbConnectionInfo(this.ConnectionString);
                    report.RecordSelectionFormula = RecordSelectionFormula;
                    report.ExportAsPdfEx(fileName, plugin.Name, true);
                    report.Close();
                }
            }

            return fileName;
        }

        private IAttachment CreateAttachment(string FileToAttach, string Description = null)
        {
            var attach = Sage.Platform.EntityFactory.Create<IAttachment>();
            attach.Description = (string.IsNullOrEmpty(Description) ? Path.GetFileName(FileToAttach) : Description);
            attach.InsertFileAttachment(FileToAttach);

            //Workaround for bug in Attachment
            var attachment = EntityFactory.GetRepository<IAttachment>();
            var Attach = attachment.FindFirstByProperty("Id", attach.Id.ToString());
            File.Move(FileToAttach, Path.Combine(this.AttachmentPath, Attach.FileName));

            return Attach;

        }

        private string AttachmentPath
        {
            get
            {
                using (var conn = new OleDbConnection(this.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = new OleDbCommand("select attachmentpath from branchoptions where sitecode = 'NOSYNCSERVER'", conn))
                    {
                        return cmd.ExecuteScalar().ToString();
                    }
                }
            }
        }

        public string ConnectionString
        {
            get
            {
                return ApplicationContext.Current.Services.Get<IDataService>().GetConnectionString();
            }
        }

        private string SanitizeFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }
    }
}
