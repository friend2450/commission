﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using OfficeOpenXml;
using Commission.Utility;

namespace Commission.Forms
{
    public partial class FrmImpSales : Form
    {
        public FrmImpSales()
        {
            InitializeComponent();
        }

        private void button_Exit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void button_OpneExcel_Click(object sender, EventArgs e)
        {
            if (openFileDialog_Excel.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                label_State.Text = "状态：读取中...";
                button_OpneExcel.Enabled = false;
                this.Refresh();

                string fileName = openFileDialog_Excel.FileName;

                FileInfo existingFile = new FileInfo(fileName);

                try
                {
                    ExcelPackage package = new ExcelPackage(existingFile);
                    int vSheetCount = package.Workbook.Worksheets.Count; //获取总Sheet页

                    ExcelWorksheet worksheet = package.Workbook.Worksheets[1];//选定 指定页

                    //int maxColumnNum = worksheet.Dimension.End.Column;//最大列
                    int maxColumnNum = 7;//最大列
                    int minColumnNum = worksheet.Dimension.Start.Column;//最小列

                    int maxRowNum = worksheet.Dimension.End.Row;//最大行
                    int minRowNum = worksheet.Dimension.Start.Row;//最小行

                    DataTable vTable = new DataTable();


                    for (int i = 0; i < maxColumnNum; i++)
                    {
                        DataColumn vC = new DataColumn("Col" + i.ToString());
                        vTable.Columns.Add(vC);
                    }

                    for (int n = 2; n <= maxRowNum; n++)
                    {
                        DataRow vRow = vTable.NewRow();
                        for (int m = 1; m <= maxColumnNum; m++)
                        {
                            vRow[m - 1] = worksheet.Cells[n, m].Value;
                        }
                        vTable.Rows.Add(vRow);
                    }

                    dataGridView_data.DataSource = vTable;
                }
                catch (Exception vErr)
                {
                    MessageBox.Show(vErr.Message);
                }
            }

            label_State.Text = "状态：准备";
            button_OpneExcel.Enabled = true;
            this.Refresh();
        }

        private void button_Import_Click(object sender, EventArgs e)
        {
            if (!DataValidate())
            {
                return;
            }

            button_Import.Enabled = false;

            label_State.Text = "状态：导入中...";
            this.Refresh();
            for (int i = 0; i < dataGridView_data.Rows.Count; i++)
            {
                string indate = dataGridView_data.Rows[i].Cells[2].Value.ToString().Equals("") ? "NULL" : "'" + dataGridView_data.Rows[i].Cells[2].Value.ToString() + "'";
                string outdate = dataGridView_data.Rows[i].Cells[3].Value.ToString().Equals("") ? "NULL" : "'" + dataGridView_data.Rows[i].Cells[3].Value.ToString() + "'";

                string valSales = Login.User.ProjectID + ",'" + Login.User.ProjectName + "','"
                    + dataGridView_data.Rows[i].Cells[0].Value.ToString() + "','"
                    + dataGridView_data.Rows[i].Cells[1].Value.ToString() + "',"
                    + indate + ","
                    + outdate;

                string sql = string.Format("insert into Sales (ProjectID, ProjectName, SalesName, Phone, InDate, OutDate) output inserted.SalesID values ({0})", valSales);

                try
                {
                    string salesId = SqlHelper.ExecuteScalar(sql).ToString();

                    string sqlJobTrack = string.Format("insert into JobTrack (SalesID, DeptID, DeptName, JobType, BeginDate)  values "
                        + "({0},{1},'{2}','{3}', {4})",
                        salesId, dataGridView_data.Rows[i].Cells[4].Value.ToString(), 
                        dataGridView_data.Rows[i].Cells[5].Value.ToString(), 
                        dataGridView_data.Rows[i].Cells[6].Value.ToString(),
                        indate);

                    SqlHelper.ExecuteNonQuery(sqlJobTrack);
                    Close();
                }
                catch (Exception ex)
                {
                    button_Import.Enabled = true;
                    Prompt.Error("导入错误：" + ex.Message.ToString());
                    continue;
                }

            }

            label_State.Text = "状态：导入完成！";
            this.Refresh();
            Prompt.Information("操作成功，数据已导入！");

            button_Import.Enabled = true;
        }

        private bool DataValidate()
        {
            bool result = true;

            for (int i = 0; i < dataGridView_data.Rows.Count; i++)
            {
                if (dataGridView_data.Rows[i].Cells[4].Value.ToString() == "")
                {
                    dataGridView_data.CurrentCell = dataGridView_data.Rows[i].Cells[4];
                    Prompt.Warning("置业顾问部门不能为空");
                    return false;
                }

                if (dataGridView_data.Rows[i].Cells[6].Value.ToString() == "")
                {
                    dataGridView_data.CurrentCell = dataGridView_data.Rows[i].Cells[6];
                    Prompt.Warning("置业顾问岗位类型不能为空");
                    return false;
                }

                string deptID = dataGridView_data.Rows[i].Cells[4].Value.ToString();

                int QueryResult = int.Parse(SqlHelper.ExecuteScalar(string.Format("select COUNT(DeptID)  from Department where DeptID = '{0}'", deptID)).ToString());

                if (QueryResult == 0)
                {
                    dataGridView_data.CurrentCell = dataGridView_data.Rows[i].Cells[4];
                    dataGridView_data.Rows[i].Selected = true;
                    Prompt.Error("没有匹配的部门信息，无法导入，请检查后，重新操作！");
                    return false;
                }

            }

            return result;

        }

        private void button_ExportDict_Click(object sender, EventArgs e)
        {
            string sql = string.Format("select DeptID '部门ID', DeptName '部门名称'  from Department where ProjectID = {0}", Login.User.ProjectID);

            Common.Exp2XLS(SqlHelper.ExecuteDataTable(sql));
        }
    }
}
