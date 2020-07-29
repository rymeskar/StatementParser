﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

using StatementParser.Attributes;

using TaxReporterCLI.Exporters.CzechMinistryOfFinance;
using TaxReporterCLI.Models.Converters;
using TaxReporterCLI.Models.Views;

namespace TaxReporterCLI
{
    internal class Output
    {
        private Dictionary<string, List<object>> GroupTransactions(IList<object> transactions)
        {
            return transactions.GroupBy(i => i.GetType()).ToDictionary(k => k.Key.Name, i => i.Select(a => a).ToList());
        }

        public void PrintAsJson(IList<object> transactions)
        {
            var groupedTransactions = GroupTransactions(transactions);

            Console.WriteLine(JsonConvert.SerializeObject(groupedTransactions));
        }

        public void SaveAsExcelSheet(string filePath, IList<object> transactions)
        {
            var groupedTransactions = GroupTransactions(transactions);

            using FileStream file = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
            var wb1 = new XSSFWorkbook();

            foreach (var group in groupedTransactions)
            {
                wb1.Add(CreateSheet(wb1, @group.Key, @group.Value));
            }

            wb1.Write(file);
        }

        public void SaveAsXml(string originalTaxDeclarationFilePath, string newTaxDeclarationFilePath, IList<object> transactions)
        {
            var converter = new DividendBrokerSummaryViewConverter();

            var groupedTransactions = GroupTransactions(transactions);

            var dividendBrokerSummeryView = groupedTransactions[nameof(DividendBrokerSummaryView)].Cast<DividendBrokerSummaryView>().ToList();
            var esppTransactionView = groupedTransactions[nameof(ESPPTransactionView)].Cast<ESPPTransactionView>().ToList();
            var depositTransactionView = groupedTransactions[nameof(DepositTransactionView)].Cast<DepositTransactionView>().ToList();
            var saleTransactionView = groupedTransactions[nameof(SaleTransactionView)].Cast<SaleTransactionView>().ToList();

            var importer = new CzechMinistryOfFinanceImporter();
            var declaration = importer.Load(originalTaxDeclarationFilePath);

            var builder = new TaxDeclarationBuilder(declaration);
            builder.WithTaxYear(2019);
            var newDeclaration = builder.Build();
            newDeclaration.TaxDeclaration.AppendixSeznam = converter.ConvertToAppendixSeznamRows(dividendBrokerSummeryView);
            newDeclaration.TaxDeclaration.AppendixIncomeTable = converter.ConvertToAppendixIncomeTableRow(esppTransactionView, depositTransactionView);
            newDeclaration.TaxDeclaration.Appendix3Lists = converter.ConvertToAppendix3List(dividendBrokerSummeryView);
            newDeclaration.TaxDeclaration.Appendix2 = converter.ConvertToAppendix2(saleTransactionView);
            newDeclaration.TaxDeclaration.Appendix2OtherIncomeRow = converter.ConvertToAppendix2OtherIncomeRow(saleTransactionView);
            newDeclaration.TaxDeclaration.Section2.Row38 = newDeclaration.TaxDeclaration.Appendix3Lists.Select(i => i.Income).Sum();
            newDeclaration.TaxDeclaration.Section2.Row40 = newDeclaration.TaxDeclaration.Appendix2.TotalProfit;

            var exporter = new CzechMinistryOfFinanceExporter();
            exporter.Save(newTaxDeclarationFilePath, newDeclaration);
        }

        public void PrintAsPlainText(IList<object> transactions)
        {
            var groupedTransactions = GroupTransactions(transactions);

            foreach (var group in groupedTransactions)
            {
                Console.WriteLine();
                Console.WriteLine(group.Key);
                Console.WriteLine(String.Join("\r\n", group.Value));
            }
        }

        private ISheet CreateSheet(XSSFWorkbook workbook, string sheetName, IList<object> objects)
        {
            var sheet = workbook.CreateSheet(sheetName);

            var headerProperties = CollectPublicProperties(objects[0]);
            var haeders = headerProperties.Select(i => DescriptionAttribute.ConstructDescription(i.Key, objects[0])).ToList();
            CreateRow(sheet, 0, haeders);

            for (int rowIndex = 1; rowIndex <= objects.Count; rowIndex++)
            {
                var properties = CollectPublicProperties(objects[rowIndex - 1]);
                CreateRow(sheet, rowIndex, properties.Values.ToList());
            }

            return sheet;
        }

        private IRow CreateRow(ISheet sheet, int rowIndex, IList<string> rowValues)
        {
            var row = sheet.CreateRow(rowIndex);

            var columnIndex = 0;
            foreach (var rowValue in rowValues)
            {
                var cell = row.CreateCell(columnIndex);

                // In case it's a number lets store it as a number
                if (Decimal.TryParse(rowValue, out var value))
                {
                    cell.SetCellValue((double)value);
                }
                else
                {
                    cell.SetCellValue(rowValue);
                }

                columnIndex++;
            }

            return row;
        }

        private IDictionary<PropertyInfo, string> CollectPublicProperties(Object instance)
        {
            var properties = instance.GetType().GetProperties();

            var dictionary = properties.Reverse().ToDictionary(
                k => k,
                i => i.GetValue(instance));

            var output = new Dictionary<PropertyInfo, string>();
            foreach (var pair in dictionary)
            {
                if (pair.Value is null)
                {
                    output.Add(pair.Key, null);
                }
                else if (pair.Value is IFormattable || pair.Value is string)
                {
                    output.Add(pair.Key, pair.Value.ToString());
                }
                else
                {
                    foreach (var prop in CollectPublicProperties(pair.Value))
                    {
                        output.Add(prop.Key, prop.Value);
                    }
                }
            }

            return output;
        }
    }
}