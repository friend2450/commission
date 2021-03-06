﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using OfficeOpenXml;
using System.IO;
using Commission.Utility;
using System.Data.SqlClient;

namespace Commission.Forms
{
    public partial class FrmImpContract : Form
    {
        DataTable dtImpData = new DataTable();

        public FrmImpContract()
        {
            InitializeComponent();
        }

        private void toolStripButton_Back_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void FrmImpSubscribe_Load(object sender, EventArgs e)
        {
        }


        private DataTable OpenSourceFile()
        {
            if (openFileDialog_Source.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                toolStripLabel_Status.Text = "状态：准备";

                string fileName = openFileDialog_Source.FileName;

                FileInfo existingFile = new FileInfo(fileName);

                dtImpData = new DataTable();

                try
                {
                    ExcelPackage package = new ExcelPackage(existingFile);
                    int iSheetCount = package.Workbook.Worksheets.Count; //获取总Sheet页

                    ExcelWorksheet worksheet = package.Workbook.Worksheets[1];//

                    int maxXlsColumnNum = worksheet.Dimension.End.Column;  //最大列
                    int minXlsColumnNum = worksheet.Dimension.Start.Column;//最小列

                    int maxXlsRowNum = worksheet.Dimension.End.Row;        //最大行
                    //int minXlsRowNum = worksheet.Dimension.Start.Row;    //最小行

                    Console.WriteLine("---------------" +dataGridView_Contract.Columns.Count);

                    foreach (DataGridViewColumn dgvCol in dataGridView_Contract.Columns)
                    {
                        Console.WriteLine(dgvCol.Name);
                        DataColumn col = new DataColumn(dgvCol.Name);
                        dtImpData.Columns.Add(col);
                    }

                    for (int rowIdx = 2; rowIdx <= maxXlsRowNum; rowIdx++)
                    {
                        DataRow row = dtImpData.NewRow();

                        for (int colIdx = 1; colIdx <= maxXlsColumnNum; colIdx++)
                        {
                            row[colIdx - 1] = worksheet.Cells[rowIdx, colIdx].Value;
                        }

                        dtImpData.Rows.Add(row);
                    }
                }
                catch (Exception vErr)
                {
                    MessageBox.Show(vErr.Message);
                }
            }

            return dtImpData;
        }

        private void toolStripButton_Close_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void toolStripButton_Open_Click(object sender, EventArgs e)
        {
            dataGridView_Contract.DataSource = OpenSourceFile();
            
        }

        private void toolStripButton_Imp_Click(object sender, EventArgs e)
        {
            toolStripButton_Imp.Enabled = false;


            toolStripLabel_Status.Text = "状态：数据导入中...";


            this.Refresh();


            if (!DataValidate())
            {
                toolStripButton_Imp.Enabled = true;


                toolStripLabel_Status.Text = "状态：准备";

                return;
            }

            string sql = string.Empty;




            using (SqlConnection connection = SqlHelper.OpenConnection())
            {
                SqlTransaction sqlTran = connection.BeginTransaction();
                SqlCommand cmd = connection.CreateCommand();
                cmd.Transaction = sqlTran;


                string timestamp = DateTime.Parse(SqlHelper.ExecuteScalar("select GetDate()").ToString()).ToString("yyMMddHHmmss");

                try
                {
                    foreach (DataRow row in dtImpData.Rows)
                    {
                        //认购主表
                        cmd.CommandText = GenSqlSubMain(row, timestamp);
                        string subscribeID = cmd.ExecuteScalar().ToString();

                        //认购从表
                        cmd.CommandText = GenSqlSubDetail(row, subscribeID, -1);
                        cmd.ExecuteNonQuery();

                        for (int i = 0; i < 3; i++)
                        {
                            cmd.CommandText = GenSqlSubDetail(row, subscribeID, i);
                            if (!cmd.CommandText.Equals(""))
                                cmd.ExecuteNonQuery();
                        }

                        //签约主表
                        cmd.CommandText = GenSqlConMain(row, subscribeID, timestamp);
                        string contractID = cmd.ExecuteScalar().ToString();

                        //签约从表
                        //主售房源
                        cmd.CommandText = GenSqlConDetail(row, contractID, -1);
                        cmd.ExecuteNonQuery();

                        //附属房源，固定3个
                        for (int i = 0; i < 3; i++)
                        {
                            cmd.CommandText = GenSqlConDetail(row, contractID, i);
                            if (!cmd.CommandText.Equals(""))
                                cmd.ExecuteNonQuery();
                        }

                        //更新签约主售房源的结算设置、提点、主售房源销售状态（签约）
                        cmd.CommandText = GetSqlSettleMng(row["ConItemID"].ToString(), row["PaymentID"].ToString(), row["SubscribeDate"].ToString());
                        cmd.ExecuteNonQuery();

                        //房源销售状态（附属）
                        for (int i = 0; i < 3; i++)
                        {
                            string itemID = row["ConItemID" + i].ToString();
                            if (!itemID.Equals(""))
                            {
                                cmd.CommandText = string.Format("update SaleItem Set SaleStateCode = 3, SaleStateName = '签约' where ItemID = {0}", itemID);
                                cmd.ExecuteNonQuery();
                            }
                        }


                        //更新认购主表的ContractID
                        cmd.CommandText = string.Format("update SubscribeMain set ContractID = {0} where SubscribeID = {1}", contractID, subscribeID);
                        cmd.ExecuteNonQuery();

       

                        //收款记录 首付
                        cmd.CommandText = GenSqlReceipt(row, contractID, false); 
                        if (cmd.CommandText != "")
                            cmd.ExecuteNonQuery();

                        //收款记录 贷款
                        cmd.CommandText = GenSqlReceipt(row, contractID, true); 
                        if (cmd.CommandText != "")
                            cmd.ExecuteNonQuery();

                        double loanAmount = 0;
                        double.TryParse(row["RecLoan"].ToString(), out loanAmount);

                        if (loanAmount > 0)
                        {
                            cmd.CommandText = string.Format("update ContractMain set LoanDate = CONVERT(varchar(10), getdate(), 120) where ContractID = {0}", contractID);
                            cmd.ExecuteNonQuery();
                        }

                    }

                    sqlTran.Commit();
                }
                catch (Exception ex)
                {
                    sqlTran.Rollback();
                    Prompt.Error("操作失败， 错误：" + ex.Message);
                }
                finally
                {
                    toolStripLabel_Status.Text = "状态：导入完成";
                    toolStripButton_Imp.Enabled = true;
                    DataTable dt = (DataTable)dataGridView_Contract.DataSource;
                    dt.Rows.Clear();
                    dataGridView_Contract.DataSource = dt;
                    Prompt.Information("操作完成!");
                }

            }


        }

        /// <summary>
        /// 生成认购主表SQL语句
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        private string GenSqlSubMain(DataRow row, string timestamp)
        {
            string sql = string.Empty;

            string fields = "SubscribeNum, ProjectID, ProjectName, CustomerID, CustomerName, CustomerPhone, SubscribeDate, "
                + "TotalAmount, SalesID, SalesName, MakeDate, UserID, UserName, Import ";
                
                //+ "ExtField0,ExtField1,ExtField2,ExtField3,ExtField4,ExtField5,ExtField6,ExtField7,ExtField8,ExtField9";

            //row[""]

            string values = "'" + row["SubscribeNum"] + "'"
                + "," + Login.User.ProjectID
                + ",'" + Login.User.ProjectName + "'"
                + "," + row["CustomerID"]
                + ",'" + row["CustomerName"] + "'"
                + ",'" + row["CustomerPhone"] + "'"
                + ",'" + row["SubscribeDate"] + "'"
                + ",'" + row["SubTotalAmount"] + "'"
                + "," + row["SubSalesID"]
                + ",'" + row["SubSalesName"] + "'"
                + ",getDate()"
                + "," + Login.User.UserID
                + ",'" + Login.User.UserName + "'"
                + ",'" + timestamp + "'";  

            sql = "insert into [SubscribeMain] (" + fields + ") output inserted.SubscribeID values (" + values + ")";

            return sql;
        }


        /// <summary>
        /// 生成认购明细SQL语句
        /// </summary>
        /// <param name="row"></param>
        /// <param name="subscribeID"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        private string GenSqlSubDetail(DataRow row, string subscribeID, int index)
        {
            string sql = string.Empty;
            string values = string.Empty;

            string itemID = string.Empty;
            
            if (index >= 0)
            {
                itemID = row["SubItemID" + index].ToString();
            }
            else
            {
                itemID = row["SubItemID"].ToString();
            }

            if (itemID.Equals("")) //为空视为无记录
                return sql;

            sql = string.Format("select ItemTypeCode, ItemTypeName from SaleItem where ItemID = {0}", itemID);
            DataTable dt = SqlHelper.ExecuteDataTable(sql);

            string typeCode = dt.Rows[0]["ItemTypeCode"].ToString();
            string typeName = dt.Rows[0]["ItemTypeName"].ToString(); ;

            string fields = "SubscribeID, RowID, ItemID, ItemTypeCode, ItemTypeName, IsBind, Building, Unit, ItemNum, Area, Price, Amount ";

            values = subscribeID + "," + index + "," + itemID + "," + typeCode + ",'" + typeName + "'," ;

            if (index >= 0)
            {
                values += "1,'','','" + row["SubItemNum" + index].ToString() + "',"
                    + row["SubArea" + index].ToString() + "," + row["SubPrice" + index].ToString() + "," + row["SubAmount" + index].ToString();
            }
            else
            {
                values += "0,'" + row["SubBuilding"].ToString() + "','" + row["SubUnit"].ToString() + "','" + row["SubItemNum"].ToString() + "',"
                    + row["SubArea"].ToString() + "," + row["SubPrice"].ToString() + "," + row["SubAmount"].ToString();
            }

            sql = string.Format("insert into SubscribeDetail ({0}) values ({1})", fields, values);

            return sql;
        }

        /// <summary>
        /// 生成签约主表SQL
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        private string GenSqlConMain(DataRow row, string subscribeID, string timestamp)
        {
            string sql = string.Empty;

            sql = string.Format("select paytype from PaymentMode where ProjectID = {0} and ID = {1} ", Login.User.ProjectID, row["PaymentID"]);
            string payType = SqlHelper.ExecuteScalar(sql).ToString();

            string fields = "SubscribeID, SubscribeDate, SubscribeSalesID, SubscribeSalesName, ContractNum, ProjectID, ProjectName, " 
                + "CustomerID, CustomerName, CustomerPhone, ContractDate, "
                + "PaymentID, PaymentName, PaymentType, DownPayRate, DownPayAmount, Loan, TotalAmount, SalesID, SalesName, MakeDate, UserID, UserName, Import ";

            //+ "ExtField0,ExtField1,ExtField2,ExtField3,ExtField4,ExtField5,ExtField6,ExtField7,ExtField8,ExtField9";

            string downpayRate = row["DownPayRate"].ToString() == "" ? "0" : row["DownPayRate"].ToString();
            string downPay = row["DownPayAmount"].ToString() == "" ? "0" : row["DownPayAmount"].ToString();
            string loan = row["Loan"].ToString() == "" ? "0" : row["Loan"].ToString();

            string values = subscribeID 
                + ",'" + row["SubscribeDate"] + "'"
                + "," + row["SubSalesID"]
                + ",'" +  row["SubSalesName"] + "'"
                + ",'" + row["ContractNum"] + "'"
                + "," + Login.User.ProjectID
                + ",'" + Login.User.ProjectName + "'"
                + "," + row["CustomerID"]
                + ",'" + row["CustomerName"] + "'"
                + ",'" + row["CustomerPhone"] + "'"
                + ",'" + row["ContractDate"] + "'"
                + "," + row["PaymentID"]
                + ",'" + row["PaymentName"] + "'"
                + "," + payType
                + "," + downpayRate
                + "," + downPay
                + "," + loan
                + ",'" + row["ConTotalAmount"] + "'"
                + "," + row["ConSalesID"]
                + ",'" + row["ConSalesName"] + "'"
                + ",getDate()"
                + "," + Login.User.UserID
                + ",'" + Login.User.UserName + "'"
                + ",'" + timestamp + "'";

            sql = "insert into [ContractMain] (" + fields + ") output inserted.ContractID values (" + values + ")";

            return sql;
        }

        /// <summary>
        /// 签约明细
        /// </summary>
        /// <param name="row"></param>
        /// <param name="contractID"></param>
        /// <param name="index">index小于0，为主售房源 </param>
        /// <returns></returns>
        private string GenSqlConDetail(DataRow row, string contractID, int index)
        {
            string sql = string.Empty;
            string values = string.Empty;

            string itemID = string.Empty;

            if (index >= 0)
            {
                itemID = row["ConItemID" + index].ToString();
            }
            else
            {
                itemID = row["ConItemID"].ToString();
            }

            if (itemID.Equals("")) //为空视为无记录
                return sql;

            sql = string.Format("select ItemTypeCode, ItemTypeName from SaleItem where ItemID = {0}", itemID);
            DataTable dt = SqlHelper.ExecuteDataTable(sql);

            string typeCode = dt.Rows[0]["ItemTypeCode"].ToString();
            string typeName = dt.Rows[0]["ItemTypeName"].ToString(); ;

            string fields = "ContractID, RowID, ItemID, ItemTypeCode, ItemTypeName, IsBind, Building, Unit, ItemNum, Area, Price, Amount ";

            values = contractID + "," + index + "," + itemID + "," + typeCode + ",'" + typeName + "',";

            if (index >= 0)
            {
                values += "1,'','','" + row["ConItemNum" + index].ToString() + "',"
                    + row["ConArea" + index].ToString() + "," + row["ConPrice" + index].ToString() + "," + row["ConAmount" + index].ToString();
            }
            else
            {
                values += "0,'" + row["ConBuilding"].ToString() + "','" + row["ConUnit"].ToString() + "','" + row["ConItemNum"].ToString() + "',"
                    + row["ConArea"].ToString() + "," + row["ConPrice"].ToString() + "," + row["ConAmount"].ToString();
            }

            sql = string.Format("insert into ContractDetail ({0}) values ({1})", fields, values);

            return sql;
        }

        
        /// <summary>
        /// 生成SQL － 更新签约主售房源的结算设置、提点、主售房源销售状态（签约）
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        private string GetSqlSettleMng(string itemID, string paymentID, string subscribeDate)
        {
            string sql = string.Empty;

            double rate = 0;
            double amount = 0;

            GetSettleRate(itemID, subscribeDate, out rate, out amount);


            sql = string.Format("select ID, PayName, PayType, PayTypeName, StandardCode, StandardName, BaseCode, BaseName from PaymentMode where ID = {0}", paymentID);

            DataTable dt = SqlHelper.ExecuteDataTable(sql);

            sql = string.Format("update SaleItem Set PayModeID = {0}, PayModeName = '{1}', PayTypeCode = {2},PayTypeName = '{3}', "
                + " SettleStandardCode = {4}, SettleStandardName = '{5}', SettleBaseCode = {6}, SettleBaseName = '{7}', "
                + " SettleRate = {8}, SettleAmount = {9}, SaleStateCode = 3, SaleStateName = '签约' where ItemID = {10}",
                paymentID, dt.Rows[0]["PayName"].ToString(), dt.Rows[0]["PayType"].ToString(), dt.Rows[0]["PayTypeName"].ToString(),
                dt.Rows[0]["StandardCode"].ToString(), dt.Rows[0]["StandardName"].ToString(), dt.Rows[0]["BaseCode"].ToString(), dt.Rows[0]["BaseName"].ToString(), 
                rate, amount, itemID);

            return sql;
            
        }

        private void  GetSettleRate(string itemID , string subscribeDate, out double rate, out double amount)
        {
            rate = 0;
            amount = 0;

            string sql = string.Empty;

            //匹配房源类型  
            sql = string.Format("select CommissionType, Rate, Amount  from SchemeRate a inner join SaleItem b on a.ItemTypeCode = b.ItemTypeCode where ItemID = {0} and ('{1}' >= BeginDate and '{1}' <= EndDate) and a.ProjectID = {2}", itemID, subscribeDate, Login.User.ProjectID);
            SqlDataReader sdr = SqlHelper.ExecuteReader(sql);
            if (sdr.Read())
            {
                rate = double.Parse(sdr["Rate"].ToString());
                amount = double.Parse(sdr["Amount"].ToString());
            }
            else
            {
                //项目默认
                sql = string.Format("select CommissionType, Rate, Amount from SchemeRate where ItemTypeCode = 0 and ('{1}' >= BeginDate and '{1}' <= EndDate) and ProjectID = {2}", itemID, subscribeDate, Login.User.ProjectID);
                sdr = SqlHelper.ExecuteReader(sql);
                if (sdr.Read())
                {
                    rate = double.Parse(sdr["Rate"].ToString());
                    amount = double.Parse(sdr["Amount"].ToString());
                }
            }

            sdr.Close();
        }


        /// <summary>
        /// 收款信息
        /// </summary>
        /// <param name="row"></param>
        /// <param name="contractID"></param>
        /// <param name="isLoan"></param>
        /// <returns></returns>
        private string GenSqlReceipt(DataRow row, string contractID, bool isLoan)
        {
            string sql = string.Empty;
            double amount = 0;
            string typeCode = string.Empty;
            string typeName = string.Empty;

            string loan = isLoan ? "1" : "0";

            string settled = "0";

            if (row["Settled"].ToString().Equals("1"))
            {
                settled = "-1";
            }

            string date = string.Empty; ;

            string fields = "ContractID, ProjectID, Amount, RecDate, TypeCode, TypeName, IsLoan, SettleState, SalesID, SalesName, MakeDate, Maker";

            if (isLoan)
            {
                date = row["LoanDate"].ToString();
                double.TryParse(row["RecLoan"].ToString(), out amount);
                typeCode = "0";
                typeName = "贷款";

            }
            else
            {
                date = row["DownPayDate"].ToString();
                double.TryParse(row["RecDownPay"].ToString(), out amount);
                typeCode = "2";
                typeName = "首付";
            }

            //金额为0不新增记录
            if (amount == 0)
                return "";

            if (date.Equals(""))
                date = "null";
            else
                date = "'" + date + "'";

            string values = contractID + ","
                + Login.User.ProjectID + ","
                + amount + ","
                + date + ","
                + typeCode + ",'"
                + typeName + "',"
                + loan + ","
                + settled + ","
                + row["ConSalesID"] + ",'"
                + row["ConSalesName"] + "',"
                + "GETDATE(),'"
                + Login.User.UserName + "'"; 


            return sql = string.Format("insert into Receipt ({0}) values ({1})", fields, values);
        }


        private bool DataValidate()
        {
            bool result = true;

            if (dataGridView_Contract.Rows.Count == 0)
            {
                Prompt.Error("没有数据记录，无法导入！");
                return false;
            }

            for (int i = 0; i < dtImpData.Rows.Count; i++)
            {
                
                if (dtImpData.Rows[i]["CustomerID"].ToString().Trim().Equals(""))
                {
                    dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["CustomerID"];
                    Prompt.Error("客户ID不能为空");
                    return false;
                }
                else
                {
                    string sql = string.Format("select Count(CustomerID) from Customer where ProjectID = {0} and  CustomerID = {1}", Login.User.ProjectID, dtImpData.Rows[i]["CustomerID"].ToString());
                    if (!Convert.ToBoolean(SqlHelper.ExecuteScalar(sql)))
                    {
                        dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["CustomerID"];
                        Prompt.Error("不存在此客户ID，请确认后再导入！");
                        return false;
                    }
                }


                if (dtImpData.Rows[i]["ConItemID"].ToString().Trim().Equals(""))
                {
                    dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["ConItemID"];
                    Prompt.Error("房源ID不能为空");
                    return false;
                }
                else
                {
                    string sql = string.Format("select SaleStateCode from SaleItem where ProjectID = {0} and  ItemID = {1}", Login.User.ProjectID, dtImpData.Rows[i]["ConItemID"].ToString());

                    object objResult = SqlHelper.ExecuteScalar(sql);

                    if (objResult == null)
                    {
                        dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["ConItemID"];
                        Prompt.Error("不存在此房源ID，请确认后再导入！");
                        return false;
                    }
                    else
                    {
                        if (objResult.ToString() != "1")
                        {
                            dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["ConItemID"];
                            Prompt.Error("此房源ID非待售状态，请确认后再导入！");
                            return false;
                        }
                    }
                }


                if (dtImpData.Rows[i]["ConSalesID"].ToString().Trim().Equals(""))
                {
                    dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["ConSalesID"];
                    Prompt.Error("签约顾问ID不能为空");
                    return false;
                }
                else
                {
                    string sql = string.Format("select Count(SalesID) from Sales where ProjectID = {0} and  SalesID = {1}", Login.User.ProjectID, dtImpData.Rows[i]["ConSalesID"].ToString());
                    if (!Convert.ToBoolean(SqlHelper.ExecuteScalar(sql)))
                    {
                        dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["ConSalesID"];
                        Prompt.Error("不存在此签约顾问ID，请确认后再导入！");
                        return false;
                    }
                }

                if (dtImpData.Rows[i]["PaymentID"].ToString().Trim().Equals(""))
                {
                    dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["PaymentID"];
                    Prompt.Error("付款方式ID不能为空");
                    return false;
                }
                else
                {
                    string sql = string.Format("select Count(ID) from PaymentMode where ProjectID = {0} and  ID = {1}", Login.User.ProjectID, dtImpData.Rows[i]["PaymentID"].ToString());
                    if (!Convert.ToBoolean(SqlHelper.ExecuteScalar(sql)))
                    {
                        dataGridView_Contract.CurrentCell = dataGridView_Contract.Rows[i].Cells["PaymentID"];
                        Prompt.Error("不存在此付款方式ID，请确认后再导入！");
                        return false;
                    }
                }
            }

            return result;
        }



        private void toolStripButton_DictCustomer_Click(object sender, EventArgs e)
        {
            string sql = string.Format("select CustomerID '客户ID', CustomerName '客户名称', CustomerPhone '客户电话', PID '身份证号', Addr '地址' from Customer  where ProjectID = {0}", Login.User.ProjectID);

            Common.Exp2XLS(SqlHelper.ExecuteDataTable(sql));
        }

        private void toolStripButton_DictSales_Click(object sender, EventArgs e)
        {
            string sql = string.Format("select SalesID '置业顾问ID', SalesName '置业顾问名称', Phone '电话', InDate '入职日期', OutDate '离职日期', Position '职位' from Sales where ProjectID = {0}", Login.User.ProjectID);

            Common.Exp2XLS(SqlHelper.ExecuteDataTable(sql));
        }

        private void toolStripButton_SaleItem_Click(object sender, EventArgs e)
        {
            string sql = string.Format("select ItemID '房源ID', Building '楼号', Unit '单元', ItemNum '房号', Area '面积', Price '单价', ItemTypeName '类型' from SaleItem where ProjectID = {0}", Login.User.ProjectID);

            Common.Exp2XLS(SqlHelper.ExecuteDataTable(sql));
        }

        private void toolStripButton_Payment_Click(object sender, EventArgs e)
        {
            string sql = string.Format("select ID '付款方式ID', PayName '付款方式名称', PayTypeName '付款类型', DownPayRate '首付比例', StandardName '结算条件', BaseName '结算基础' from PaymentMode  where ProjectID = {0}", Login.User.ProjectID);

            Common.Exp2XLS(SqlHelper.ExecuteDataTable(sql));
        }

        private void FrmImpContract_Shown(object sender, EventArgs e)
        {
            try
            {
                //DataGridViewCellStyle dgvcs = new DataGridViewCellStyle();
                //dgvcs.BackColor = Color.Red;

                //dataGridView_Contract.Columns["ColCustomerID"].DefaultCellStyle = dgvcs;
                //dataGridView_Contract.Columns["ColItemID"].DefaultCellStyle = dgvcs;
                //dataGridView_Contract.Columns["ColSalesID"].DefaultCellStyle = dgvcs;


                //dataGridView_Contract.Columns["ColCustomerID"].DefaultCellStyle.BackColor = Color.Red;
                //dataGridView_Contract.Columns["ColItemID"].DefaultCellStyle.BackColor = Color.Red;
                //dataGridView_Contract.Columns["ColSalesID"].DefaultCellStyle.BackColor = Color.Red;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error Message: " + ex.Message);
            }
            finally
            {
                //MessageBox.Show("装载完成！");
            }

        }





    }
}
