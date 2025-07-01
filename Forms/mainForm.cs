using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Word = Aspose.Words;
using Label = System.Windows.Forms.Label;
using System.Drawing.Drawing2D;
using System.Data.OleDb;
using System.IO;
using Microsoft.Web.WebView2.Core;


namespace TestingApp.Forms
{
    public partial class mainForm : Form
    {
        private Student studNow;
        private Teacher teachNow;
        private static List<string> ans;
        private static List<Quest> questions;
        private Timer timer;
        private int remainingTime;
        private string nowTestName;
        private Button lastClickButtonNavigate = null;
        private Point offset = new Point(0, 0); 
        private Point dragStart = Point.Empty; 
        private bool isDragging = false;
        private bool isDrawEndTest = false;
        private Bitmap treeBitmap;
        private Node cachedRootNode;
        private string[] endTestData;
        private int countColumn = 3;

        public mainForm()
        {
            InitializeComponent();
        }

        private void Panel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStart = e.Location; 
                (sender as Panel).Cursor = Cursors.Hand; 
            }
        }

        private void UpdateTree(Panel panel)
        {
            treeBitmap = null; 
            cachedRootNode = null;
            offset.X = 0;
            offset.Y = 0;
            panel.Invalidate();
            EnableDoubleBuffering(panel);
        }

        private void Panel_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                offset.X += e.X - dragStart.X;
                offset.Y += e.Y - dragStart.Y;

                dragStart = e.Location;

                (sender as Panel).Invalidate();
            }
        }

        private void Panel_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
                (sender as Panel).Cursor = Cursors.Default;
            }
        }

        private void GraphPanel_Paint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;

            if (treeBitmap == null || cachedRootNode == null)
            {
                string[] tag = panel.Tag.ToString().Split(';');
                cachedRootNode = GetRootNode(tag[0], tag[1], bool.Parse(tag[2]), tag[3]);

                if (cachedRootNode != null)
                {
                    treeBitmap = new Bitmap(1920, 1080);

                    using (Graphics g = Graphics.FromImage(treeBitmap))
                    {
                        g.Clear(panel.BackColor);

                        DrawTree(g, cachedRootNode, new Point(treeBitmap.Width / 2, cachedRootNode.Height / 2 + 30));
                    }
                }
            }

            if (treeBitmap != null)
            {
                e.Graphics.DrawImage(treeBitmap, offset.X - treeBitmap.Width / 2 + panel.Width / 2, offset.Y);
            }
        }

        private Node GetRootNode(string rootNodeName, string studentOrGroupId, bool isGroup, string prepod)
        {
            string[] rootQuery = Query($"SELECT ID FROM Tems WHERE Name = '{rootNodeName}'");
            if (rootQuery.Length == 0 || string.IsNullOrWhiteSpace(rootQuery[0]))
            {
                Console.WriteLine("Root topic not found.");
                return null;
            }
            int rootId = int.Parse(rootQuery[0]);

            return BuildNodeTree(rootId, studentOrGroupId, isGroup, prepod);
        }

        private Node BuildNodeTree(int _nodeId, string _studentOrGroupId, bool _isGroup, string _prepod)
        {
            Node rootNode = Build(_nodeId, _studentOrGroupId, _isGroup);

            if (_prepod != "")
            {
                List<string> teachTest = Query($@"SELECT Name 
                                          FROM Tems, Teachers_Tests, Teachers
                                          WHERE Tems.ID  = TemsID AND 
                                                Teachers.ID = TeacherID AND 
                                                Fio = '{_prepod}'").ToList();

                List<Node> removeList = new List<Node>();
                traversal(rootNode);

                void traversal(Node node, Node parent = null)
                {
                    if (node.Child.Count == 0)
                    {
                        if (!teachTest.Contains(node.Theme))
                            removeList.Add(node);
                        return;
                    }

                    foreach (Node ch in node.Child)
                        traversal(ch, node);

                    foreach (Node ch in removeList)
                        if (node.Child.Contains(ch))
                            node.Child.Remove(ch);

                    if (node.Child.Count == 0)
                        removeList.Add(node);
                }
            }
            CalculateResults(rootNode, _nodeId, _studentOrGroupId, _isGroup);


            void CalculateResults(Node node, int nodeId, string studentOrGroupId, bool isGroup)
            {
                foreach (Node childNode in node.Child)
                {
                    string[] childIds = Query($"SELECT Name, ChildID FROM TemsParentChild, Tems WHERE Tems.ID = ChildID AND ParentID = {nodeId}");

                    Dictionary<string, int> chDict = new Dictionary<string, int>();
                    for (int i = 0; i < childIds.Length; i += 2)
                        chDict[childIds[i]] = int.Parse(childIds[i + 1]);

                    CalculateResults(childNode, chDict[childNode.Theme], studentOrGroupId, isGroup);
                }

                string[] resultsQuery;
                double completeness = 0, integrity = 0, skills = 0;

                if (isDrawEndTest)
                {
                    resultsQuery = endTestData;
                    isDrawEndTest = false;
                }
                else if (_isGroup)
                {
                    resultsQuery = Query($"SELECT AVG(Poln), AVG([Full]), AVG(Skills) " +
                                          $"FROM ResultTests, Students, [Group] " +
                                          $"WHERE ResultTests.StudID = Students.ID AND " +
                                                $"[Group].ID = Students.GroupID AND " +
                                                $"TemsID = {nodeId} AND " +
                                                $"GroupNum = '{studentOrGroupId}'");
                }
                else
                {
                    resultsQuery = Query($"SELECT Poln, [Full], Skills " +
                                          $"FROM ResultTests, Students " +
                                          $"WHERE ResultTests.StudID = Students.ID AND " +
                                              $"TemsID = {nodeId} AND Mail = '{studentOrGroupId}'");
                }

                if (resultsQuery.Length >= 3)
                {
                    completeness = Math.Round(double.TryParse(resultsQuery[0], out var c) ? c / 100 : 0, 2);
                    integrity = Math.Round(double.TryParse(resultsQuery[1], out var i) ? i / 100 : 0, 2);
                    skills = Math.Round(double.TryParse(resultsQuery[2], out var s) ? s / 100 : 0, 2);
                }

                if (resultsQuery.Length < 3 || string.IsNullOrWhiteSpace(resultsQuery[0]))
                {
                    if (node.Child.Count > 0)
                    {
                        completeness = Math.Round(node.Child.Average(c => c.Completeness), 2);
                        integrity = Math.Round(node.Child.Average(c => c.Integrity), 2);
                        skills = Math.Round(node.Child.Average(c => c.Skills), 2);
                    }
                }

                node.Completeness = completeness;
                node.Integrity = integrity;
                node.Skills = skills;
            }


            Node Build(int nodeId, string studentOrGroupId, bool isGroup)
            {
                List<Node> children = new List<Node>();
                string[] childIds = Query($"SELECT ChildID FROM TemsParentChild WHERE ParentID = {nodeId}");
                string nameTeme = Query($"SELECT Name FROM Tems WHERE ID = {nodeId}")[0];
                foreach (string childIdStr in childIds)
                {
                    if (int.TryParse(childIdStr, out int childId))
                    {
                        Node childNode = Build(childId, studentOrGroupId, isGroup);
                        if (childNode != null)
                            children.Add(childNode);
                    }
                }

                return new Node(nameTeme, 0, 0, 0, children);
            }


            return rootNode;
        }

        private void EnableDoubleBuffering(Control control)
        {
            typeof(Control).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, control, new object[] { true });
        }

        private void DrawTree(Graphics g, Node node, Point position)
        {
            Point shiftedPosition = new Point(position.X + offset.X, position.Y + offset.Y);

            if (countColumn == 2)
                node.Draw2(g, shiftedPosition);
            else
                node.Draw3(g, shiftedPosition);

            if (node.Child != null && node.Child.Count > 0)
            {
                int totalChildWidth = CalculateSubtreeWidth(node);
                int startX = position.X - totalChildWidth / 2;
                int verticalSpacing = 50;

                foreach (var child in node.Child)
                {
                    Point childPosition = new Point(
                        startX + child.Width / 2,
                        position.Y + node.Height + verticalSpacing
                    );

                    Point shiftedChildPosition = new Point(
                        childPosition.X + offset.X,
                        childPosition.Y + offset.Y
                    );

                    g.DrawLine(
                        Pens.Black,
                        shiftedPosition.X,
                        shiftedPosition.Y + node.Height / 2,
                        shiftedChildPosition.X,
                        shiftedChildPosition.Y - child.Height / 2
                    );

                    DrawTree(g, child, childPosition);

                    startX += CalculateSubtreeWidth(child);
                }
            }
        }


        private int CalculateSubtreeWidth(Node node)
        {
            if (node.Child == null || node.Child.Count == 0 || node.Child.Count == 1)
            {
                return node.Width;
            }

            int totalWidth = 0;
            foreach (var child in node.Child)
            {
                totalWidth += CalculateSubtreeWidth(child);
            }

            return totalWidth;
        }

        private static string[] Query(string query)
        {
            string dbPath = @"..\..\..\DB\DB.accdb";

            string connectionString = $@"
            Provider=Microsoft.ACE.OLEDB.12.0;
            Data Source={Path.GetFullPath(dbPath)};
            Persist Security Info=False;";

            string ans = "";

            try
            {
                using (OleDbConnection connection = new OleDbConnection(connectionString))
                {
                    connection.Open();

                    using (OleDbCommand command = new OleDbCommand(query, connection))
                        using (OleDbDataReader reader = command.ExecuteReader())
                            while (reader.Read())
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                    ans += $"{reader[i]}~";
                            }
                            if (ans.Length != 0)
                                ans = ans.Substring(0, ans.Length - 1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }

            return ans.Split('~');
        }

        static void QueryNoReturn(string query)
        {
            string dbPath = @"..\..\..\DB\DB.accdb";

            string connectionString = $@"
            Provider=Microsoft.ACE.OLEDB.12.0;
            Data Source={Path.GetFullPath(dbPath)};
            Persist Security Info=False;";

            try
            {
                using (OleDbConnection connection = new OleDbConnection(connectionString))
                {
                    connection.Open();

                    using (OleDbCommand command = new OleDbCommand(query, connection))
                    {
                        int rowsAffected = command.ExecuteNonQuery();

                        Console.WriteLine($"Количество добавленных записей: {rowsAffected}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        private void textLog_log_TextChanged(object sender, EventArgs e)
        {
            labIncr_log.Visible = false;
        }

        private void textPass_log_MaskInputRejected(object sender, MaskInputRejectedEventArgs e)
        {
            labIncr_log.Visible = false;
        }

        private void butExit_Click(object sender, EventArgs e)
        {
            panIntel_teacher.Paint -= GraphPanel_Paint;
            panIntel_teacher.MouseDown -= Panel_MouseDown;
            panIntel_teacher.MouseUp -= Panel_MouseUp;
            panIntel_teacher.MouseMove -= Panel_MouseMove;
            panIntel_teacher.CreateGraphics().Clear(Color.White);

            panTests_stud.Paint -= GraphPanel_Paint;
            panTests_stud.MouseDown -= Panel_MouseDown;
            panTests_stud.MouseUp -= Panel_MouseUp;
            panTests_stud.MouseMove -= Panel_MouseMove;
            panTests_stud.CreateGraphics().Clear(Color.White);

            ClearLoginReg();
            tabControl.SelectTab(login);
        }

        private void res_exit_Click(object sender, EventArgs e)
        {
            ClearPanel(panTests_stud);
            FillStudentPage();
            tabControl.SelectTab(student);
        }

        private void end_test_Click(object sender, EventArgs e)
        {
            timer.Stop();
            Result();
            tabControl.SelectTab(result);
        }

        private void rbStud_reg_CheckedChanged(object sender, EventArgs e)
        {
            lablGroup_reg.Visible = true;
            textGroup_reg.Visible = true;
        }

        private void rbTeach_reg_CheckedChanged(object sender, EventArgs e)
        {
            lablGroup_reg.Visible = false;
            textGroup_reg.Visible = false;
        }

        private void buttReg_Click(object sender, EventArgs e)
        {
            if (!IsValidRegisterData())
            {
                labWar_reg.Visible = true;
            }
            else
            {
                if (rbStud_reg.Checked)
                {
                    studNow = new Student(textMail_reg.Text, textPass_reg.Text, textGroup_reg.Text, textFam_reg.Text, textName_reg.Text, textOtch_reg.Text);
                    AddStudent(studNow);
                    FillStudentPage();
                    tabControl.SelectTab(student);
                }
                else
                {
                    teachNow = new Teacher(textMail_reg.Text, textPass_reg.Text, textFam_reg.Text, textName_reg.Text, textOtch_reg.Text);
                    AddTeacher(teachNow);
                    FillTeacherPage();
                    tabControl.SelectTab(teacher);
                }
            }
        }

        private bool IsValidRegisterData()
        {
            if (textFam_reg.Text == "" ||
                textName_reg.Text == "" ||
                textOtch_reg.Text == "" ||
                textMail_reg.Text == "" ||
                CheckPass(textPass_reg.Text) ||
                !(rbTeach_reg.Checked || rbStud_reg.Checked) ||
                (textGroup_reg.Text == "" && rbStud_reg.Checked))
            {
                labWar_reg.Text = "Заполните все поля";
                return false;
            }

            else if (Query($@"SELECT IIF(SUM(IIF(Students.Fio = '{textFam_reg.Text} {textName_reg.Text} {textOtch_reg.Text}' OR 
                                                 Teachers.Fio = '{textFam_reg.Text} {textName_reg.Text} {textOtch_reg.Text}' OR 
                                                 Students.Mail = '{textMail_reg.Text}' OR 
                                                 Teachers.Mail = '{textMail_reg.Text}', 1, 0)) > 0, 'true', 'false')
                              FROM Students, Teachers")[0] == "true")
            {
                labWar_reg.Text = "Такой пользователь уже существует";
                return false;
            }

            else
                return true;
        }
        private bool CheckPass(string pass)
        {
            char[] banChars = { ' ', '/', '\\', '|', ',', '"', '\'', '\n', '\t', ';', ':', '?', '<', '>', '*', '(', ')', '$', '%', '#', '@', '^'};
            return pass.Any(el => banChars.Contains(el));
        }

        private void AddStudent(Student newStudent)
        {
            if (Query($"SELECT ID FROM [Group] WHERE GroupNum = '{newStudent.Group}'")[0] == "")
                QueryNoReturn($@"INSERT INTO [Group] (GroupNum)
                                 VALUES ('{newStudent.Group}')");

            QueryNoReturn($@"INSERT INTO Students (Mail, [Password], Fio, GroupID)
                             SELECT '{newStudent.Mail}', '{newStudent.Password}', '{newStudent.Fio}', ID 
                             FROM [Group] 
                             WHERE GroupNum = '{newStudent.Group}'"); 
        }

        private void AddTeacher(Teacher newTeacher)
        {
            QueryNoReturn($@"INSERT INTO Teachers (Mail, [Password], Fio)
                             VALUES ('{newTeacher.Mail}', '{newTeacher.Password}', '{newTeacher.Fio}')");
        }

        private void labLogin_reg_Click(object sender, EventArgs e)
        {
            tabControl.SelectTab(login);
            labWar_reg.Visible = false;
            labWar_log.Visible = false;
        }

        private void labReg_log_Click(object sender, EventArgs e)
        {
            tabControl.SelectTab(register);
            labWar_reg.Visible = false;
            labWar_log.Visible = false;
        }

        private void butEntr_log_Click(object sender, EventArgs e)
        {
            if (textLog_log.Text != "" && textPass_log.Text != "")
            {
                labWar_log.Visible = false;

                string idTeacDB = Query($"SELECT ID FROM Teachers WHERE Mail = '{textLog_log.Text}' AND [Password] = '{textPass_log.Text}'")[0];
                string idStudDB = Query($"SELECT ID FROM Students WHERE Mail = '{textLog_log.Text}' AND [Password] = '{textPass_log.Text}'")[0];

                if (idTeacDB != "")
                {
                    string[] teacherData = Query($"SELECT Mail, [Password], Fio FROM Teachers WHERE ID = {idTeacDB}");
                    teachNow = new Teacher(teacherData[0], teacherData[1], teacherData[2]);
                    FillTeacherPage();
                    tabControl.SelectTab(teacher);
                    return;
                }

                if (idStudDB != "")
                {
                    string[] studentData = Query($@"SELECT Mail, [Password], GroupNum, Fio 
                                                    FROM Students, [Group] 
                                                    WHERE Students.GroupID = [Group].ID AND
                                                          Students.ID = {idStudDB}");
                    string[] testDataQuery = Query($@"SELECT Name, Attempts, Poln, [Full], Skills
                                                      FROM ResultTests, Tems
                                                      WHERE Tems.ID = TemsID AND 
                                                            StudID = {idStudDB}");
                    Dictionary<string, List<int>> testData = new Dictionary<string, List<int>>();
                    if (testDataQuery.Length != 1)
                        for (int i = 0; i < testDataQuery.Length; i += 5)
                            testData[testDataQuery[i]] = new List<int>() { int.Parse(testDataQuery[i + 1]),
                                                                           int.Parse(testDataQuery[i + 2]),
                                                                           int.Parse(testDataQuery[i + 3]),
                                                                           int.Parse(testDataQuery[i + 4]) };

                    studNow = new Student(studentData[0], studentData[1], studentData[2], studentData[3], testData); 
                    FillStudentPage();
                    tabControl.SelectTab(student);
                    return;
                }

                labIncr_log.Visible = true;
            }
            else
                labWar_log.Visible = true;
        }

        private void CreateTestPanelStudent(string _name, string _status, int _attempt, Panel pastPan, Panel parent)
        {
            Point loc = new Point(0, 0);
            int tag = 0;

            if (pastPan != null)
            {
                tag = int.Parse(pastPan.Tag.ToString()) + 1;
                loc.Y = (pastPan.Height + 3) * tag ;
            }

            Panel panel = new Panel() 
            {
                Size = new Size(350, 72),
                Tag = tag,
                BorderStyle = BorderStyle.Fixed3D,
                Location = loc,
            };
            Label name = new Label() 
            { 
                Text = _name,
                AutoSize = false,
                Size = new Size(180, 34),
                Font = new Font("Microsoft Sans Serif", 9),
                Location = new Point(2, 5),
            };
            Label status = new Label()
            {
                Text = _status,
                AutoSize = false,
                Size = new Size(120, 22),
                Font = new Font("Microsoft Sans Serif", 13),
                Location = new Point(185, 5),
            };
            Label preAttempt = new Label()
            {
                Text = "Попыток осталось:",
                AutoSize = false,
                Size = new Size(134, 17),
                Font = new Font("Microsoft Sans Serif", 10),
                Location = new Point(3, 45)
            };
            Label attempt = new Label()
            {
                Text = _attempt.ToString(),
                AutoSize = false,
                Size = new Size(16, 17),
                Font = new Font("Microsoft Sans Serif", 10),
                Location = new Point(143, 45)
            };
            Button info = new Button()
            {
                Text = "Информация",
                AutoSize = false,
                Size = new Size(81, 23),
                Location = new Point(185, 42),

            };
            Button startTest = new Button()
            {
                Text = "Пройти",
                Location = new Point(265, 42),
                Enabled = attempt.Text != "0"
            };

            info.Click += (s, e) => InfoTestStudent(_name);
            startTest.Click += (s, e) => StartTest(_name);

            panel.Controls.Add(name);
            panel.Controls.Add(status);
            panel.Controls.Add(attempt);
            panel.Controls.Add(preAttempt);
            panel.Controls.Add(info);
            panel.Controls.Add(startTest);

            panTests_stud.Controls.Add(panel);
        }
        private void CreateStudIntelMap(Panel parent, string studMail, string testName)
        {
            DeleteIntelMap(parent);
            UpdateTree(parent);
            parent.Tag = $"{testName};{studMail};false;";

            if (testName == "МИСПИС")
                countColumn = 2;
            else
                countColumn = 3;

            parent.Paint += GraphPanel_Paint;
            parent.MouseDown += Panel_MouseDown;
            parent.MouseUp += Panel_MouseUp;
            parent.MouseMove += Panel_MouseMove;
        }

        private Panel GetLastTestPanel(Panel parent)
        {
            Panel lastChild = null;
            int maxTag = -1;

            foreach (Control control in parent.Controls)
                if (control is Panel panel && panel.Tag is int tag && tag > maxTag)
                {
                    maxTag = tag;
                    lastChild = panel;
                }

            return lastChild;
        }

        private void FillStudentPage() 
        {
            string[] testNames = Query(@"SELECT DISTINCT Name
                                         FROM Questions, Tems
                                         WHERE TemsID = Tems.ID");

            foreach (string testName in testNames)
            {
                string name = testName;
                string status = "Не пройдено";
                int attemp = 3;

                for (int i = 0; i < studNow.Tests.Count; i++)
                    if (studNow.Tests.Keys.Contains(name))
                    {
                        status = "Пройдено";
                        attemp = studNow.Tests[name][0];
                    }

                CreateTestPanelStudent(name, status, attemp, GetLastTestPanel(panTests_stud), panTests_stud);
            }

            labFio_stud.Text = studNow.Fio;
            ShowTheoryTree();
        }

        private void ShowTheoryTree()
        {
            treeView1.Nodes.Clear();
            List<TreeTheory> rootNodeDict = new List<TreeTheory>();

            string[] query = Query("SELECT Name, Ref FROM Tems");
            for (int i = 0; i < query.Length; i += 2)
                rootNodeDict.Add(new TreeTheory(query[i], query[i + 1]));

            Dictionary<string, TreeTheory> treeTheoryDict = new Dictionary<string, TreeTheory>();
            foreach (TreeTheory node in rootNodeDict)
            {
                treeTheoryDict[node.Name] = node;
            }

            query = Query(@"SELECT parent.Name, child.Name
                            FROM Tems as parent, Tems as child, TemsParentChild
                            WHERE ParentID = parent.ID AND ChildID = child.ID");
            for (int i = 0; i < query.Length; i += 2)
                treeTheoryDict[query[i]].Child.Add(treeTheoryDict[query[i + 1]]);

            
            Dictionary<string, TreeNode> addedNodes = new Dictionary<string, TreeNode>();

            foreach (TreeTheory node in treeTheoryDict.Values)
            {
                if (addedNodes.ContainsKey(node.Name))
                    continue;

                TreeNode treeNode = AddNodeRecursive(node, addedNodes);

                if (treeNode.Nodes.Count == 0)
                    foreach (string el in node.Data.Keys)
                    {
                        treeNode.Nodes.Add(new TreeNode(el));
                    }
                if (treeNode.Parent == null)
                    treeView1.Nodes.Add(treeNode);
            }

            TreeNode AddNodeRecursive(TreeTheory node, Dictionary<string, TreeNode> addedNodess)
            {
                if (addedNodess.ContainsKey(node.Name))
                    return addedNodess[node.Name];

                TreeNode treeNode = new TreeNode(node.Name);
                addedNodess[node.Name] = treeNode;

                foreach (TreeTheory child in node.Child)
                {
                    TreeNode childNode = AddNodeRecursive(child, addedNodess);
                    if (childNode.Nodes.Count == 0)
                        foreach (string el in child.Data.Keys)
                        {
                            childNode.Nodes.Add(new TreeNode(el));
                        }
                    treeNode.Nodes.Add(childNode);
                }

                return treeNode;
            }
            treeView1.NodeMouseClick += (sender, e) => CreateLectionViewer(e, rootNodeDict);
        }

        private void CreateLectionViewer(TreeNodeMouseClickEventArgs e, List<TreeTheory> rootNodeList)
        {
            foreach (Control control in pageLectures.Controls.OfType<WebView2>().ToList())
            {
                pageLectures.Controls.Remove(control);
                control.Dispose();
            }

            string urlRaw = "";
            string urlEnd = "";

            var r = e.Node.Text;

            foreach (TreeTheory rootNode in rootNodeList)
                FindData(rootNode);

            if (urlRaw == "")
                return;
            else if (urlRaw.Contains("http://") || urlRaw.Contains("https://"))
                urlEnd = urlRaw;
            else
                urlEnd = DocxToPdfPath(urlRaw);

            WebView2 webView = new WebView2
            {
                Location = new Point(262, 3),
                Size = new Size(533, 366)
            };
            pageLectures.Controls.Add(webView);

            webView.Source = new Uri(urlEnd);

            void FindData(TreeTheory element)
            {
                foreach (var data in element.Data)
                    if (data.Key == e.Node.Text)
                    {
                        urlRaw = data.Value;
                        return;
                    }
                foreach (var child in element.Child)
                    FindData(child);
            }
        }

        private string DocxToPdfPath(string docxPath)
        {
            string pdfPath = "C:\\Users\\nikit\\Работы\\Study\\МИСПИС\\TestingApp\\.theory\\temp.pdf";
             Word.Document w = new Word.Document(docxPath);
            w.Save(pdfPath, Word.SaveFormat.Pdf);
            return pdfPath;
        }

        private void ClearLoginReg()
        {
            textLog_log.Text = "";
            textPass_log.Text = "";
            textFam_reg.Text = "";
            textName_reg.Text = "";
            textOtch_reg.Text = "";
            textMail_reg.Text = "";
            textPass_reg.Text = "";
            textGroup_reg.Text = "";
            rbStud_reg.Checked = false;
            rbTeach_reg.Checked = false;
        }

        private void DeleteIntelMap(Panel parent)
        {
            foreach (Control control in parent.Controls.OfType<Panel>().ToList())
            {
                panInfo.Controls.Remove(control);
                panIntel_teacher.Controls.Remove(control);
            }
        }

        private void ClearPanel(Panel parent)
        {
            parent.Controls.Clear();
        }

        private void InfoTestStudent(string testName)
        {
            labName_stud.Text = testName;

            labAutor_stud.Text = Query($@"SELECT Fio
                                          FROM Teachers_Tests, Tems, Teachers
                                          WHERE TeacherID = Teachers.ID AND 
                                                TemsID = Tems.ID AND 
                                                Name = '{testName}'")[0];

            panInfo.Visible = true;

            foreach (string names in studNow.Tests.Keys)
                if (testName == names)
                {
                    labStat_stud.Text = "Пройдено";
                    labAttn_stud.Text = studNow.Tests[testName][0].ToString();
                    panInfo_Intel.Tag = labAttn_stud.Text;
                    CreateStudIntelMap(panInfo_Intel, studNow.Mail, testName);
                    return;
                }

            labStat_stud.Text = "Не пройдено";
            labAttn_stud.Text = "3";
        }

        private void StartTest(string testName)
        {
            nowTestName = testName;
            LoadTest();
            tabControl.SelectTab(test);
            StartTimer();
        }

        private void LoadTest()
        {
            int countQuestion = 9;

            if (nowTestName == "МИСПИС")
                countQuestion = 6;

            lastClickButtonNavigate = null;

            ans = new List<string>(countQuestion);
            for (int i = 0; i < countQuestion; i++)
                ans.Add("");

            remainingTime = (countQuestion / 2 +
                            (countQuestion / 3 * 3) +
                            (countQuestion - countQuestion / 2 - countQuestion / 3) * 5) * 60;

            questions = GetRandQuestList(countQuestion);
            ShowQuestion(0);
            ShowNavigationButtons(countQuestion);
        }

        private List<Quest> GetRandQuestList(int countQuestion)
        {
            Random rand = new Random();

            string[] query = Query($@"SELECT Type, Complexity, Question, Choice, Answer 
                                      FROM Questions, Tems
                                      WHERE Tems.ID = TemsID AND
                                            Name = '{nowTestName}'");
            Dictionary<string, Dictionary<string, List<Quest>>> questions = new Dictionary<string, Dictionary<string, List<Quest>>>();
            string[] type = new string[] { "poln", "full", "skil" };
            string[] comp = new string[] { "simp", "medi", "hard" };
            foreach (string t in type)
            {
                questions[t] = new Dictionary<string, List<Quest>>();
                foreach(string c in comp)
                    questions[t][c] = new List<Quest>();
            }

            for (int i = 0; i < query.Length; i += 5)
                questions[query[i]][query[i + 1]].Add(new Quest(query[i], query[i + 1], query[i + 2], query[i + 3].Split(';').ToList(), query[i + 4].Split(';').ToList()));

            List<Quest> randomQuestionList = new List<Quest>();
            //if (nowTestName == "МИСПИС")
            //{
            //    for (int i = 0; i < countQuestion / 2; i++)
            //        foreach (string t in new string[] { "poln", "full" })
            //            randomQuestionList.Add(questions[t]["simp"][rand.Next(0, questions[t]["simp"].Count)]);
            //}
            //else
            //{
            //    foreach (string t in type)
            //        foreach (string c in comp)
            //            randomQuestionList.Add(questions[t][c][rand.Next(0, questions[t][c].Count)]);
            //}

            string[] types = (nowTestName == "МИСПИС") ? new string[] { "poln", "full" } : new string[] { "poln", "full", "skil" };
            foreach (string t in types)
                foreach (string c in comp)
                    randomQuestionList.Add(questions[t][c][rand.Next(0, questions[t][c].Count)]);

            return randomQuestionList;
        }

        private void ShowNavigationButtons(int countQuestion)
        {
            NavigationPanel1.ColumnStyles.Clear();
            NavigationPanel1.Controls.Clear();
            NavigationPanel1.ColumnCount = countQuestion;

            for (int i = 0; i < countQuestion; i++)
            {
                NavigationPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / countQuestion));
            }

            for (int i = 0; i < countQuestion; i++)
            {
                Button questionButt = new Button
                {
                    Text = (i + 1).ToString(),
                    Dock = DockStyle.Fill,
                    BackColor = SystemColors.Control,
                    UseVisualStyleBackColor = true
                };
                questionButt.Click += (s, e) => ClickNavigation(s);
                NavigationPanel1.Controls.Add(questionButt, i, 0);
            }
        }

        private void ClickNavigation(object sender)
        {
            if (lastClickButtonNavigate != null)
                lastClickButtonNavigate.BackColor = SystemColors.Control;

            lastClickButtonNavigate = sender as Button;
            lastClickButtonNavigate.UseVisualStyleBackColor = true;
            lastClickButtonNavigate.BackColor = Color.FromArgb(235,235,235);

            ShowQuestion(int.Parse((sender as Button).Text) - 1);
        }

        private void ShowQuestion(int page)
        {
            QuestPan.Controls.Clear();
            Quest question = questions[page];

            Label questionLabel = new Label
            {
                Text = question.Question,
                Dock = DockStyle.Top,
                AutoSize = false,
                Font = new Font("Arial", 14, System.Drawing.FontStyle.Bold),
                Width = QuestPan.Size.Width,
                Height = QuestPan.Size.Height - 150,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            QuestPan.Controls.Add(questionLabel);

            FlowLayoutPanel radioPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(10),
            };

            if (question.Choice.Count > 1 && question.Answer.Count == 1)
            {
                for (int i = 0; i < question.Choice.Count; i++)
                {
                    RadioButton radioButton = new RadioButton
                    {
                        Text = question.Choice[i],
                        AutoSize = true,
                        Font = new Font("Arial", 12),
                        Padding = new Padding(5),
                        Tag = i,
                        Checked = ans[page] == i.ToString() ? true : false
                    };
                    radioButton.Click += (s, e) => ans[page] = (s as RadioButton).Tag.ToString();
                    radioPanel.Controls.Add(radioButton);
                }
            }
            else if (question.Answer.Count >= 2 && question.Choice.Count >= 2)
            {
                for (int i = 0; i < question.Choice.Count; i++)
                {
                    CheckBox checkBox = new CheckBox
                    {
                        Text = question.Choice[i],
                        AutoSize = true,
                        Font = new Font("Arial", 12),
                        Padding = new Padding(5),
                        Tag = i,
                        Checked = ans[page].Contains(i.ToString()) ? true : false
                    };

                    checkBox.Click += (s, e) =>
                    {
                        if ((s as CheckBox).Checked)
                            ans[page] += (s as CheckBox).Tag.ToString();
                        else
                            ans[page] = ans[page].Replace((s as CheckBox).Tag.ToString(), "");
                    };

                    radioPanel.Controls.Add(checkBox);
                }
            }
            else if (question.Choice.Count == 1 && question.Choice[0].Length == 0)
            {
                TextBox textBox = new TextBox
                {
                    Height = 25,
                    Font = new Font("Arial", 12),
                    Padding = new Padding(5),
                    Width = QuestPan.Width - 20,
                    Dock = DockStyle.Bottom,
                    Text = ans[page]
                };
                textBox.Leave += (s, e) => ans[page] = (s as TextBox).Text;
                radioPanel.Controls.Add(textBox);
            }
            QuestPan.Controls.Add(radioPanel);
        }

        private void StartTimer()
        {
            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += TimerTick;
            timer.Start();
        }
        private void TimerTick(object sender, EventArgs e)
        {
            if (remainingTime > 0)
            {
                timeLabel.Text = $"Осталось времени: {TimerOut(remainingTime)} сек";
                remainingTime--;
            }
            else
            {
                timer.Stop();
            }
        }
        private string TimerOut(int sec)
        {
            string rez = "";

            if ((sec / 60).ToString().Length == 1)
                rez += $"0{sec / 60}:";
            else
                rez += $"{sec / 60}:";

            if ((sec % 60).ToString().Length == 1)
                rez += $"0{sec % 60}";
            else
                rez += $"{sec % 60}";

            return rez;
        }

        private void Result()
        {
            double poln = 0;
            double full = 0;
            double skil = 0;
            //int countPoln = 0;
            //int countFull = 0;
            //int countSkil = 0;

            for (int i = 0; i < questions.Count; i++)
            {
                double point = 0;
                switch (questions[i].Complexity)
                {
                    case "simp":
                        if (ans[i] == questions[i].Answer[0])
                            point = 1 / 3.0;
                        break;
                    case "medi":

                        double help = 0;
                        foreach (char el in ans[i])
                        {
                            if (questions[i].Answer.Contains(char.ToString(el)))
                                help++;
                            else
                                help--;
                        }
                        point = (Math.Max(0, help) / questions[i].Answer.Count) * 0.5;
                        break;
                    case "hard":
                        if (ans[i] == questions[i].Answer[0])
                            point = 1;
                        break;
                    default: break;
                }

                switch (questions[i].Type)
                {
                    case "poln": poln = Math.Max(point, poln); break;
                    case "full": full = Math.Max(point, full); break;
                    case "skil": skil = Math.Max(point, skil); break;
                }
            }
                //switch (questions[i].Complexity)
                //{
                //    case "simp": point = 1; break;
                //    case "medi": point = 2; break;
                //    case "hard": point = 3; break;
                //    default: break;
                //}

                //    switch (questions[i].Type)
                //    {
                //        case "poln":

                //            if (ans[i] == questions[i].Answer[0])
                //                poln += point;
                //            countPoln += point; 

                //            break;
                //        case "full":

                //            double help = 0;
                //            foreach (char el in ans[i])
                //            {
                //                if (questions[i].Answer.Contains(char.ToString(el)))
                //                    help++;
                //                else
                //                    help--;
                //            }
                //            full += (Math.Max(0, help) / questions[i].Answer.Count) * point;
                //            countFull += point;

                //            break;
                //        case "skil":

                //            if (ans[i] == questions[i].Answer[0])
                //                skil += point;
                //            countSkil += point; 

                //            break;
                //        default: break;
                //    }
                //}

            if (nowTestName == "МИСПИС")
            {
                //poln = poln / countPoln;
                //full = full / countFull;

                UpdateStud(poln, full, 0);

                CreateStudIntelMap(panel4, studNow.Mail, nowTestName);
                labRes_res.Text = $"Вы решили правильно {Math.Round((poln + full) / 0.02, 2)}%";
            }
            else
            {
                //poln = poln / countPoln;
                //full = full / countFull;
                //skil = skil / countSkil;

                UpdateStud(poln, full, skil);

                CreateStudIntelMap(panel4, studNow.Mail, nowTestName);
                labRes_res.Text = $"Вы решили правильно {Math.Round((poln + full + skil) / 0.03, 2)}%";
            }
            
        }

        private void UpdateStud(double poln, double full, double skil)
        {
            string studId = Query($"SELECT ID FROM Students WHERE Mail = '{studNow.Mail}'")[0];
            string temsId = Query($"SELECT ID FROM Tems WHERE Name = '{nowTestName}'")[0];

            if (!studNow.Tests.ContainsKey(nowTestName))
                studNow.Tests[nowTestName] = new List<int> { 3, 0, 0, 0 };

            studNow.Tests[nowTestName][0]--;
            if ((poln + full + skil) / 3 > (studNow.Tests[nowTestName].Sum() - studNow.Tests[nowTestName][0]) / 300.0)
            {
                studNow.Tests[nowTestName][1] = (int)(poln * 100);
                studNow.Tests[nowTestName][2] = (int)(full * 100);
                studNow.Tests[nowTestName][3] = (int)(skil * 100);
            }

            isDrawEndTest = true;
            endTestData = new string[] { (poln * 100).ToString(), (full * 100).ToString(), (skil * 100).ToString() };

            if (Query($"SELECT ID FROM ResultTests WHERE StudID = {studId} AND TemsID = {temsId}")[0] == "")
                QueryNoReturn($@"INSERT INTO ResultTests (StudID, TemsID, Attempts, Poln, [Full], Skills)
                                 VALUES( {studId},
                                         {temsId},
                                         {studNow.Tests[nowTestName][0]},
                                         {studNow.Tests[nowTestName][1]},
                                         {studNow.Tests[nowTestName][2]},
                                         {studNow.Tests[nowTestName][3]})");
            else
                QueryNoReturn($@"UPDATE ResultTests
                                    SET Attempts = {studNow.Tests[nowTestName][0]},
                                        Poln = {studNow.Tests[nowTestName][1]},
                                        [Full] = {studNow.Tests[nowTestName][2]},
                                        Skills = {studNow.Tests[nowTestName][3]}
                                    WHERE StudID = {studId} AND TemsID = {temsId}");
        }

        private void FillTeacherPage()
        {
            labFio_teach.Text = teachNow.Fio;
            ShowTree();
        }

        private void ShowTree()
        {
            treeView.Nodes.Clear();
            treeView.Dock = DockStyle.Fill;

            Dictionary<string, List<string>> students = new Dictionary<string, List<string>>();
            string[] allStud = Query("SELECT GroupNum, Fio From Students, [Group] WHERE GroupID = [Group].ID");

            for (int i = 0; i < allStud.Length; i += 2)
            {
                if (students.ContainsKey(allStud[i]))
                    students[allStud[i]].Add(allStud[i + 1]);
                else
                    students[allStud[i]] = new List<string>() { allStud[i + 1] };
            }

            foreach (var group in students)
            {
                TreeNode rootGroup = new TreeNode(group.Key);
                foreach (string std in group.Value)
                    rootGroup.Nodes.Add(new TreeNode(std));

                treeView.Nodes.Add(rootGroup);
            }
            treeView.NodeMouseClick += (sender, e) => NodeClick(e);
        }

        private void NodeClick(TreeNodeMouseClickEventArgs e)
        {
            ClearPanel(panIntel_teacher);
            EnableDoubleBuffering(panIntel_teacher);
            if (e.Node.Parent == null)
            {
                labGroup_teach.Text = e.Node.Text;
                labFioText_teach.Visible = false;
                labStFio_teach.Visible = false;
                CreateTeachIntelMap(panIntel_teacher, "Нейронные сети", e.Node.Text, true);
            }
            else
            {
                labGroup_teach.Text = e.Node.Parent.Text;
                labStFio_teach.Text = e.Node.Text;
                labFioText_teach.Visible = true;
                labStFio_teach.Visible = true;
                CreateTeachIntelMap(panIntel_teacher, "Нейронные сети", e.Node.Text, false);
            }
            panInfo_teach.Visible = true;
        }

        private void CreateTeachIntelMap(Panel parent, string testName, string studOrGroumName, bool isGroup)
        {
            if (!isGroup)
                parent.Tag = $"{testName};{Query($"SELECT Mail FROM Students WHERE Fio = '{studOrGroumName}'")[0]};false;{teachNow.Fio}";
            else
                parent.Tag = $"{testName};{studOrGroumName};true;{teachNow.Fio}";
            UpdateTree(parent);
            parent.Paint += GraphPanel_Paint;
            parent.MouseDown += Panel_MouseDown;
            parent.MouseUp += Panel_MouseUp;
            parent.MouseMove += Panel_MouseMove;
        }

        private void выходToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void справкаToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox1 modalForm = new AboutBox1();
            
            modalForm.ShowDialog();
        }
    }

    public class TreeTheory
    {
        public string Name { get; set; }
        public int Layer {  get; set; }
        public Dictionary<string, string> Data {get; set;}
        public List<TreeTheory> Child { get; set;}

        public TreeTheory() { }

        public TreeTheory(string name, int layer, string data)
        {
            Name = name;
            Layer = layer;
            Data = StrToDict(data);
            Child = new List<TreeTheory>();
        }
        public TreeTheory(string name, string data)
        {
            Name = name;
            Data = StrToDict(data);
            Child = new List<TreeTheory>();
        }

        private Dictionary<string, string> StrToDict(string str)
        {
            Dictionary<string, string> ret = new Dictionary<string, string>();
            int c = 1;

            foreach (string d in str.Split(','))
            {
                if (d != "")
                {
                    int index = d.IndexOf(':');

                    if (index != -1)
                    {
                        string nameData = d.Substring(0, index);
                        ret[nameData] = d.Substring(index + 1);
                    }
                    else
                        ret[$"Ссылка {c}"] = d.Substring(index + 1);

                    c++;
                }
            }

            return ret;
        }
    }

    public class Teacher
    {
        public string Mail { get; set; }
        public string Password { get; set; }
        public string Fio { get; set; }
        public List<string> Tests { get; set; }
        public List<string> Groups { get; set; }

        public Teacher() {}
        public Teacher(string mail, string pass, string fam, string name, string otc)
        {
            Mail = mail;
            Password = pass.Replace(" ", "").Replace("-", "").Replace(".", "").Replace(",", "");
            Fio = $"{fam.Replace(" ", "")} {name.Replace(" ", "")} {otc.Replace(" ", "")}";
            Tests = new List<string>();
            Groups = new List<string>();
        }
        public Teacher(string mail, string pass, string fio)
        {
            Mail = mail;
            Password = pass.Replace(" ", "").Replace("-", "").Replace(".", "").Replace(",", "");
            Fio = fio;
            Tests = new List<string>();
            Groups = new List<string>();
        }
    }

    public class Student
    {
        public string Mail { get; set; }
        public string Password { get; set; }
        public string Fio { get; set; }
        public string Group { get; set; }
        public Dictionary<string, List<int>> Tests { get; set; }

        public Student() { }

        public Student(string mail, string pass, string group, string fam, string name, string otc)
        {
            Mail = mail;
            Password = pass.Replace(" ", "").Replace("-", "").Replace(".", "").Replace(",", "");
            Fio = $"{fam.Replace(" ", "")} {name.Replace(" ", "")} {otc.Replace(" ", "")}";
            Group = group;
            Tests = new Dictionary<string, List<int>>();
        }
        public Student(string mail, string pass, string group, string fio, Dictionary<string, List<int>> tests)
        {
            Mail = mail;
            Password = pass.Replace(" ", "").Replace("-", "").Replace(".", "").Replace(",", "");
            Fio = fio;
            Group = group;
            Tests = tests;
        }
    }

    public class Quest
    {
        public string Question { get; set; }
        public List<string> Choice { get; set; }
        public List<string> Answer { get; set; }
        public string Complexity {  get; set; }
        public string Type {  get; set; }

        public Quest(string type, string complexity, string quest, List<string> choice, List<string> answer)
        {
            Type = type;
            Complexity = complexity;
            Question = quest;
            Choice = choice;
            Answer = answer;
        }
    }
    public class Node
    {
        private const int CornerRadius = 10;

        public int Width { get; set; }
        public int Height { get; set; }
        public Point Position { get; set; }
        public double Completeness { get; set; }
        public double Integrity { get; set; }
        public double Skills { get; set; }
        public List<Node> Child { get; set; }
        public string Theme { get; set; }

        public Node(string theme, double completeness, double integrity, double skills, List<Node> child)
        {
            Width = 200;
            Height = 120;
            Position = new Point(0, 0);
            Completeness = completeness;
            Integrity = integrity;
            Skills = skills;
            Child = child;
            Theme = theme;
        }

        public Node(string theme, double completeness, double integrity, double skills)
        {
            Width = 200;
            Height = 120;
            Position = new Point(0, 0);
            Completeness = completeness;
            Integrity = integrity;
            Skills = skills;
            Child = null;
            Theme = theme;
        }

        public void Draw3(Graphics g, Point pos)
        {
            Position = pos;

            Rectangle rect = new Rectangle(Position.X - Width / 2, Position.Y - Height / 2, Width, Height);
            using (GraphicsPath path = GetRoundedRectanglePath(rect, CornerRadius))
            {
                g.FillPath(Brushes.SeaShell, path);
                g.DrawPath(Pens.Black, path);
            }

            string[] labels = { "Полнота", "Целостность", "Умения" };
            double[] values = { Completeness, Integrity, Skills };
            int barWidth = Width / 4;
            int barHeight = Height / 3;
            int barPadding = Width / 16;

            for (int i = 0; i < 3; i++)
            {
                int filledHeight = (int)(values[i] * barHeight);

                Rectangle barRect = new Rectangle(
                    rect.X + barPadding + i * (barWidth + barPadding),
                    rect.Y + rect.Height - barHeight - 20,
                    barWidth,
                    barHeight
                );
                g.DrawRectangle(Pens.Black, barRect);

                Rectangle filledRect = new Rectangle(
                    barRect.X,
                    barRect.Y + barHeight - filledHeight,
                    barRect.Width,
                    filledHeight
                );
                g.FillRectangle(Brushes.LightBlue, filledRect);

                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(labels[i], SystemFonts.DefaultFont, Brushes.Black,
                    new Point(barRect.X + barRect.Width / 2, barRect.Y - 15), sf);

                StringFormat sfd = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(values[i].ToString(), SystemFonts.DefaultFont, Brushes.Black,
                    new Point(barRect.X + barRect.Width / 2, barRect.Y + barRect.Height / 2), sfd);
            }

            string theme = $"{Theme}";
            g.DrawString(theme, SystemFonts.DefaultFont, Brushes.Black,
                new Point(rect.X + rect.Width / 2, rect.Y + 15),
                new StringFormat { Alignment = StringAlignment.Center });

            double average = (Completeness + Integrity + Skills) / 3;

            string chnText = $"CHN: {average:F2}";
            g.DrawString(chnText, SystemFonts.DefaultFont, Brushes.Black,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height - 15),
                new StringFormat { Alignment = StringAlignment.Center });
        }

        public void Draw2(Graphics g, Point pos)
        {
            Position = pos;

            Rectangle rect = new Rectangle(Position.X - Width / 2, Position.Y - Height / 2, Width, Height);
            using (GraphicsPath path = GetRoundedRectanglePath(rect, CornerRadius))
            {
                g.FillPath(Brushes.SeaShell, path);
                g.DrawPath(Pens.Black, path);
            }

            string[] labels = { "Полнота", "Целостность" };
            double[] values = { Completeness, Integrity };
            int barWidth = Width / 3;
            int barHeight = Height / 3;
            int barPadding = (rect.Width - (barWidth * 2)) / 3;

            for (int i = 0; i < 2; i++)
            {
                int filledHeight = (int)(values[i] * barHeight);

                Rectangle barRect = new Rectangle(
                    rect.X + barPadding + i * (barWidth + barPadding),
                    rect.Y + rect.Height - barHeight - 20,
                    barWidth,
                    barHeight
                );
                g.DrawRectangle(Pens.Black, barRect);

                Rectangle filledRect = new Rectangle(
                    barRect.X,
                    barRect.Y + barHeight - filledHeight,
                    barRect.Width,
                    filledHeight
                );
                g.FillRectangle(Brushes.LightBlue, filledRect);

                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(labels[i], SystemFonts.DefaultFont, Brushes.Black,
                    new Point(barRect.X + barRect.Width / 2, barRect.Y - 15), sf);

                StringFormat sfd = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(values[i].ToString(), SystemFonts.DefaultFont, Brushes.Black,
                    new Point(barRect.X + barRect.Width / 2, barRect.Y + barRect.Height / 2), sfd);
            }

            string theme = $"{Theme}";
            g.DrawString(theme, SystemFonts.DefaultFont, Brushes.Black,
                new Point(rect.X + rect.Width / 2, rect.Y + 15),
                new StringFormat { Alignment = StringAlignment.Center });

            double average = (Completeness + Integrity) / 2;

            string chnText = $"CHN: {average:F2}";
            g.DrawString(chnText, SystemFonts.DefaultFont, Brushes.Black,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height - 15),
                new StringFormat { Alignment = StringAlignment.Center });
        }

        private GraphicsPath GetRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.X + rect.Width - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.X + rect.Width - radius * 2, rect.Y + rect.Height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

