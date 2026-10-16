using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Models;
using OfflineCvBioDataGenerator.Repositories;
using OfflineCvBioDataGenerator.Services;

namespace OfflineCvBioDataGenerator
{
    public partial class Form1 : Form
    {
        private readonly ClientRepository _clientRepo = new();
        private readonly EducationRepository _eduRepo = new();
        private readonly ExperienceRepository _expRepo = new();

        private int _editingClientId;
        private int _lastSelectedId;
        private string _selectedPhotoPath = string.Empty;

        /// <summary>true while the grids/form are being filled programmatically, so the
        /// grid events do not treat our own writes as user edits.</summary>
        private bool _loading;

        /// <summary>true when the form holds edits that are not written to the database yet.</summary>
        private bool _dirty;

        // UI Controls
        private TextBox txtFullName = null!;
        private TextBox txtAddress = null!;
        private TextBox txtPhone = null!;
        private TextBox txtEmail = null!;
        private DateTimePicker dtpDOB = null!;
        private TextBox txtNIC = null!;
        private DataGridView dgvEducation = null!;
        private DataGridView dgvExperience = null!;
        private DataGridView dgvClients = null!;
        private TextBox txtSearch = null!;
        private ComboBox cmbTemplate = null!;
        private TabControl tabControl = null!;
        private PictureBox pbPhoto = null!;
        private Button btnBrowsePhoto = null!;
        private OpenFileDialog openFileDialog = null!;

        public Form1()
        {
            InitializeComponent();

            DatabaseHelper.InitializeDatabase();
            PhotoStore.EnsureFolder();

            InitializeCustomUI();
            WireDirtyTracking();
            LoadClients();
        }

        // ===================================================================== UI setup

        private void InitializeCustomUI()
        {
            Text = "Offline CV & Bio-Data Generator";
            Size = new Size(950, 650);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(950, 650);

            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var dashboardGroup = new GroupBox
            {
                Text = "Main Dashboard",
                Dock = DockStyle.Top,
                Height = 200,
                Padding = new Padding(5)
            };

            var searchLabel = new Label
            {
                Text = "Search by Name or NIC:",
                Location = new Point(10, 20),
                AutoSize = true
            };

            txtSearch = new TextBox { Location = new Point(150, 18), Width = 250 };
            txtSearch.TextChanged += (_, _) => Guard(() => LoadClients());

            var btnCreateNew = new Button
            {
                Text = "Create New CV",
                Location = new Point(410, 17),
                Size = new Size(120, 25)
            };
            btnCreateNew.Click += (_, _) => Guard(CreateNew);

            dgvClients = new DataGridView
            {
                Location = new Point(10, 55),
                Size = new Size(910, 130),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvClients.Columns.Add("Id", "Id");
            dgvClients.Columns.Add("FullName", "Full Name");
            dgvClients.Columns.Add("Address", "Address");
            dgvClients.Columns.Add("Phone", "Phone");
            dgvClients.Columns.Add("Email", "Email");
            dgvClients.Columns.Add("DOB", "DOB");
            dgvClients.Columns.Add("NIC", "NIC");
            dgvClients.Columns["Id"]!.Width = 45;
            dgvClients.SelectionChanged += DgvClients_SelectionChanged;
            dgvClients.DoubleClick += (_, _) =>
            {
                if (dgvClients.SelectedRows.Count > 0) tabControl.SelectedIndex = 0;
            };

            dashboardGroup.Controls.AddRange(new Control[] { searchLabel, txtSearch, btnCreateNew, dgvClients });

            tabControl = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };

            TabPage tabPersonal = InitializePersonalTab();

            // ---------------------------------------------------------- Education tab
            var tabEducation = new TabPage("Education") { Padding = new Padding(10) };

            dgvEducation = CreateEntryGrid(new Point(10, 10), new Size(890, 200));
            dgvEducation.Columns.Add("Qualification", "Qualification");
            dgvEducation.Columns.Add("Institute", "Institute");
            dgvEducation.Columns.Add("Year", "Year");
            dgvEducation.Columns.Add("Results", "Results");

            var lblEducationHint = new Label
            {
                Text = "Rows entered here are stored with the client - use the 'Save Client' button at the bottom.",
                Location = new Point(10, 222),
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };

            tabEducation.Controls.Add(dgvEducation);
            tabEducation.Controls.Add(lblEducationHint);

            // --------------------------------------------------------- Experience tab
            var tabExperience = new TabPage("Experience") { Padding = new Padding(10) };

            dgvExperience = CreateEntryGrid(new Point(10, 10), new Size(890, 200));
            dgvExperience.Columns.Add("JobTitle", "Job Title");
            dgvExperience.Columns.Add("Company", "Company");
            dgvExperience.Columns.Add("Duration", "Duration");

            var lblExperienceHint = new Label
            {
                Text = "Rows entered here are stored with the client - use the 'Save Client' button at the bottom.",
                Location = new Point(10, 222),
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };

            tabExperience.Controls.Add(dgvExperience);
            tabExperience.Controls.Add(lblExperienceHint);

            // ----------------------------------------------------------- Generate tab
            var tabGenerate = new TabPage("Generate") { Padding = new Padding(20) };

            var lblTemplate = new Label { Text = "Select Template:", Location = new Point(10, 20), AutoSize = true };
            cmbTemplate = new ComboBox
            {
                Location = new Point(130, 18),
                Width = 420,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            // Labels come from PdfGenerator.TemplateNames so the combo box and the
            // TemplateType enum can never drift apart (a unit test pins them).
            cmbTemplate.Items.AddRange(PdfGenerator.TemplateNames);
            cmbTemplate.SelectedIndex = 0;

            var btnGenerate = new Button
            {
                Text = "Generate PDF",
                Location = new Point(130, 60),
                Size = new Size(150, 40)
            };
            btnGenerate.Click += (_, _) => Guard(GeneratePdf);

            tabGenerate.Controls.AddRange(new Control[] { lblTemplate, cmbTemplate, btnGenerate });

            tabControl.TabPages.AddRange(new[] { tabPersonal, tabEducation, tabExperience, tabGenerate });

            // --------------------------------------------------------- bottom buttons
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10) };

            var btnSave = new Button { Text = "Save Client", Location = new Point(10, 10), Size = new Size(120, 30) };
            btnSave.Click += (_, _) => Guard(SaveClient);

            var btnClear = new Button { Text = "Clear Form", Location = new Point(140, 10), Size = new Size(120, 30) };
            btnClear.Click += (_, _) => Guard(ClearForm);

            bottomPanel.Controls.AddRange(new Control[] { btnSave, btnClear });

            mainPanel.Controls.Add(tabControl);
            mainPanel.Controls.Add(dashboardGroup);
            Controls.Add(mainPanel);
            Controls.Add(bottomPanel);
        }

        private static DataGridView CreateEntryGrid(Point location, Size size)
        {
            return new DataGridView
            {
                Location = location,
                Size = size,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
        }

        /// <summary>
        /// Builds the 'Personal' tab.
        /// Layout rule:
        ///   LEFT  column (x = 10..470)  -> all text/date fields, stacked vertically.
        ///   RIGHT column (x = 540..680) -> profile photo label, preview and browse button.
        /// The two columns never intersect, so no control can overlap.
        /// </summary>
        private TabPage InitializePersonalTab()
        {
            var tabPersonal = new TabPage("Personal") { Padding = new Padding(10) };

            // ---------------- LEFT COLUMN : text fields (x = 10 / 130, width = 340) ----------------
            const int labelX = 10;
            const int fieldX = 130;
            const int fieldW = 340;   // right edge = 470, safely left of the photo column (540)

            var lblFullName = new Label { Text = "Full Name:", Location = new Point(labelX, 22), AutoSize = true };
            txtFullName = new TextBox { Location = new Point(fieldX, 19), Width = fieldW, MaxLength = ClientValidator.MaxNameLength };

            var lblAddress = new Label { Text = "Address:", Location = new Point(labelX, 60), AutoSize = true };
            txtAddress = new TextBox { Location = new Point(fieldX, 57), Width = fieldW, Multiline = true, Height = 55, ScrollBars = ScrollBars.Vertical };

            var lblPhone = new Label { Text = "Phone:", Location = new Point(labelX, 130), AutoSize = true };
            txtPhone = new TextBox { Location = new Point(fieldX, 127), Width = fieldW, MaxLength = 30 };

            var lblEmail = new Label { Text = "Email:", Location = new Point(labelX, 167), AutoSize = true };
            txtEmail = new TextBox { Location = new Point(fieldX, 164), Width = fieldW, MaxLength = 100 };

            var lblDOB = new Label { Text = "Date of Birth:", Location = new Point(labelX, 204), AutoSize = true };
            dtpDOB = new DateTimePicker
            {
                Location = new Point(fieldX, 201),
                Format = DateTimePickerFormat.Short,
                Width = 150,
                // Without a checkbox DateTimePicker.Checked is permanently true, which
                // would force DOB = today onto every new client. The box makes DOB
                // genuinely optional (matching the nullable DOB column).
                ShowCheckBox = true,
                Checked = false,
                MinDate = new DateTime(1900, 1, 1),
                MaxDate = DateTime.Today
            };

            var lblNIC = new Label { Text = "NIC:", Location = new Point(labelX, 241), AutoSize = true };
            txtNIC = new TextBox { Location = new Point(fieldX, 238), Width = fieldW, MaxLength = 30 };

            // ---------------- RIGHT COLUMN : profile photo (x = 540) ----------------
            const int photoX = 540;

            var lblPhoto = new Label { Text = "Profile Photo:", Location = new Point(photoX, 22), AutoSize = true };
            pbPhoto = new PictureBox
            {
                Location = new Point(photoX, 47),
                Size = new Size(140, 140),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            btnBrowsePhoto = new Button
            {
                Text = "Browse Photo",
                Location = new Point(photoX, 197),
                Size = new Size(140, 32)
            };
            btnBrowsePhoto.Click += (_, _) => Guard(BrowsePhoto);

            openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
                Title = "Select Profile Photo"
            };

            tabPersonal.Controls.AddRange(new Control[]
            {
                lblFullName, txtFullName,
                lblAddress, txtAddress,
                lblPhone, txtPhone,
                lblEmail, txtEmail,
                lblDOB, dtpDOB,
                lblNIC, txtNIC,
                lblPhoto, pbPhoto, btnBrowsePhoto
            });

            return tabPersonal;
        }

        /// <summary>Flags the form as edited whenever the operator changes any input.</summary>
        private void WireDirtyTracking()
        {
            EventHandler onChanged = (_, _) => { if (!_loading) _dirty = true; };

            txtFullName.TextChanged += onChanged;
            txtAddress.TextChanged += onChanged;
            txtPhone.TextChanged += onChanged;
            txtEmail.TextChanged += onChanged;
            txtNIC.TextChanged += onChanged;
            dtpDOB.ValueChanged += onChanged;

            foreach (var grid in new[] { dgvEducation, dgvExperience })
            {
                grid.CellValueChanged += (_, _) => { if (!_loading) _dirty = true; };
                grid.UserAddedRow += (_, _) => { if (!_loading) _dirty = true; };
                grid.UserDeletedRow += (_, _) => { if (!_loading) _dirty = true; };
            }
        }

        // ===================================================================== actions

        private void BrowsePhoto()
        {
            if (openFileDialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                SetPhotoImage(LoadImageWithoutLock(openFileDialog.FileName));
                _selectedPhotoPath = openFileDialog.FileName;
                _dirty = true;
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "BrowsePhoto");
                SetPhotoImage(null);
                _selectedPhotoPath = string.Empty;
                MessageBox.Show(this,
                    "That file could not be opened as an image.\n\nPlease choose a JPG or PNG file.",
                    "Invalid image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveClient()
        {
            // ---- validation ------------------------------------------------------
            var draft = BuildDraftClient();
            var errors = ClientValidator.ValidateForSave(draft);

            if (errors.Count > 0)
            {
                ShowValidationErrors(errors);
                tabControl.SelectedIndex = 0;
                txtFullName.Focus();
                return;
            }

            // ---- grid validation -------------------------------------------------
            var education = CollectEducation(out var educationErrors);
            var experience = CollectExperience(out var experienceErrors);

            var allErrors = educationErrors.Concat(experienceErrors).ToList();
            if (allErrors.Count > 0)
            {
                ShowValidationErrors(allErrors);
                return;
            }

            // ---- photo (never fatal - the operator may continue without one) ------
            bool keepExistingPhoto = false;
            if (!string.IsNullOrWhiteSpace(_selectedPhotoPath))
            {
                try
                {
                    string stored = PhotoStore.Import(_selectedPhotoPath);
                    _selectedPhotoPath = PhotoStore.Resolve(stored);
                    draft.PhotoPath = stored;
                }
                catch (Exception ex)
                {
                    ErrorLog.Write(ex, "ImportPhoto");
                    var answer = MessageBox.Show(this,
                        "The photo could not be copied into the application folder:\n\n" + ex.Message +
                        "\n\nContinue saving WITHOUT a photo?",
                        "Photo problem", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (answer == DialogResult.No) return;

                    draft.PhotoPath = string.Empty;
                }
            }
            else if (_editingClientId > 0)
            {
                keepExistingPhoto = true;
            }

            if (keepExistingPhoto)
                draft.PhotoPath = _clientRepo.GetClientById(_editingClientId)?.PhotoPath ?? string.Empty;

            // ---- write ------------------------------------------------------------
            try
            {
                _clientRepo.Save(draft, education, experience);
                _editingClientId = draft.Id;
                _lastSelectedId = draft.Id;
                _dirty = false;

                LoadClients();
                SelectClientRow(draft.Id);

                MessageBox.Show(this, "Client saved successfully!",
                    "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (InvalidOperationException ex)
            {
                // Known, user-facing problem (duplicate NIC, deleted record, ...).
                MessageBox.Show(this, ex.Message, "Cannot save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "SaveClient");
                MessageBox.Show(this,
                    "The client could not be saved.\n\nDetails: " + ex.Message,
                    "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GeneratePdf()
        {
            if (_editingClientId == 0)
            {
                MessageBox.Show(this,
                    "Please save or select a client first.",
                    "No client selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_dirty)
            {
                MessageBox.Show(this,
                    "You have unsaved changes.\n\nClick 'Save Client' first - otherwise the PDF would be generated from the previously saved data.",
                    "Unsaved changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var client = _clientRepo.GetClientById(_editingClientId);
            var errors = ClientValidator.ValidateForPdf(client);
            if (errors.Count > 0)
            {
                ShowValidationErrors(errors);
                tabControl.SelectedIndex = 0;
                return;
            }

            var detail = new ClientDetail
            {
                Client = client!,
                EducationList = _eduRepo.GetByClientId(_editingClientId),
                ExperienceList = _expRepo.GetByClientId(_editingClientId)
            };

            // A dedicated mapper keeps the index -> template mapping in one place and
            // is covered by unit tests, instead of an if/else chain here.
            TemplateType template = PdfGenerator.FromIndex(cmbTemplate.SelectedIndex);

            try
            {
                string folder = GetOutputFolder();
                string filePath = PdfGenerator.GenerateToFile(detail, template, folder);

                MessageBox.Show(this, $"PDF generated successfully:\n{filePath}",
                    "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);

                TryOpenPdf(filePath);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "GeneratePdf");
                MessageBox.Show(this,
                    "The PDF could not be generated.\n\nDetails: " + ex.Message,
                    "PDF generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TryOpenPdf(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // The file is safely written; only the viewer launch failed.
                ErrorLog.Write(ex, "OpenPdf");
                MessageBox.Show(this,
                    "The PDF was saved, but it could not be opened automatically.\n" +
                    "Please open it from the desktop (or your chosen folder).",
                    "Could not open the PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string GetOutputFolder()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop)) return desktop;

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return !string.IsNullOrWhiteSpace(documents) ? documents : AppDomain.CurrentDomain.BaseDirectory;
        }

        private void CreateNew()
        {
            if (!ConfirmDiscardChanges()) return;
            ClearFormInternal();
        }

        private void ClearForm()
        {
            if (!ConfirmDiscardChanges()) return;
            ClearFormInternal();
        }

        private void ClearFormInternal()
        {
            _editingClientId = 0;
            _lastSelectedId = 0;
            _dirty = false;

            _loading = true;
            try
            {
                txtFullName.Clear();
                txtAddress.Clear();
                txtPhone.Clear();
                txtEmail.Clear();
                txtNIC.Clear();
                dtpDOB.Value = DateTime.Today;
                dtpDOB.Checked = false;
                dgvEducation.Rows.Clear();
                dgvExperience.Rows.Clear();
                dgvClients.ClearSelection();
            }
            finally
            {
                _loading = false;
            }

            _selectedPhotoPath = string.Empty;
            SetPhotoImage(null);
        }

        // ===================================================================== loading

        private void LoadClients()
        {
            try
            {
                List<Client> clients = string.IsNullOrWhiteSpace(txtSearch.Text)
                    ? _clientRepo.GetAllClients()
                    : _clientRepo.SearchClients(txtSearch.Text.Trim());

                _loading = true;
                try
                {
                    dgvClients.Rows.Clear();
                    foreach (var client in clients)
                    {
                        dgvClients.Rows.Add(
                            client.Id,
                            client.FullName,
                            client.Address,
                            client.Phone,
                            client.Email,
                            client.DOB?.ToShortDateString() ?? "",
                            client.NIC);
                    }

                    // Clearing the selection stops SelectionChanged from overwriting the
                    // form the operator is currently typing into.
                    dgvClients.ClearSelection();
                }
                finally
                {
                    _loading = false;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "LoadClients");
                MessageBox.Show(this,
                    "The saved client list could not be loaded.\n\nDetails: " + ex.Message,
                    "Database error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DgvClients_SelectionChanged(object? sender, EventArgs e)
        {
            if (_loading) return;

            int id = GetSelectedClientId();
            if (id <= 0 || id == _editingClientId) return;

            if (!ConfirmDiscardChanges())
            {
                // Operator declined - put the selection back where it was.
                _loading = true;
                try
                {
                    dgvClients.ClearSelection();
                    if (_lastSelectedId > 0) SelectClientRowInternal(_lastSelectedId);
                }
                finally
                {
                    _loading = false;
                }
                return;
            }

            LoadClientData(id);
        }

        private void LoadClientData(int clientId)
        {
            var client = _clientRepo.GetClientById(clientId);
            if (client == null)
            {
                MessageBox.Show(this, "This client record no longer exists.", "Missing record",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LoadClients();
                return;
            }

            _editingClientId = client.Id;
            _lastSelectedId = client.Id;

            _loading = true;
            try
            {
                txtFullName.Text = client.FullName ?? string.Empty;
                txtAddress.Text = client.Address ?? string.Empty;
                txtPhone.Text = client.Phone ?? string.Empty;
                txtEmail.Text = client.Email ?? string.Empty;
                txtNIC.Text = client.NIC ?? string.Empty;
                dtpDOB.Value = ClampToPickerRange(client.DOB);
                dtpDOB.Checked = client.DOB.HasValue;

                dgvEducation.Rows.Clear();
                foreach (var edu in _eduRepo.GetByClientId(client.Id))
                    dgvEducation.Rows.Add(edu.Qualification, edu.Institute, edu.Year, edu.Results);

                dgvExperience.Rows.Clear();
                foreach (var exp in _expRepo.GetByClientId(client.Id))
                    dgvExperience.Rows.Add(exp.JobTitle, exp.Company, exp.Duration);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "LoadClientData");
                MessageBox.Show(this,
                    "This client could not be loaded.\n\nDetails: " + ex.Message,
                    "Database error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _loading = false;
            }

            LoadClientPhoto(client);
            _dirty = false;
        }

        /// <summary>
        /// DateTimePicker throws when handed a date outside MinDate/MaxDate, so an
        /// out-of-range DOB coming from the database must be clamped first.
        /// </summary>
        private DateTime ClampToPickerRange(DateTime? value)
        {
            DateTime candidate = value ?? DateTime.Today;
            if (candidate < dtpDOB.MinDate) return dtpDOB.MinDate;
            if (candidate > dtpDOB.MaxDate) return dtpDOB.MaxDate;
            return candidate;
        }

        private void LoadClientPhoto(Client client)
        {
            // Release the previous image first - otherwise every row click leaks a
            // GDI handle and keeps the old photo file locked.
            SetPhotoImage(null);
            _selectedPhotoPath = string.Empty;

            if (string.IsNullOrWhiteSpace(client.PhotoPath)) return;

            try
            {
                string fullPath = PhotoStore.Resolve(client.PhotoPath);
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return;

                SetPhotoImage(LoadImageWithoutLock(fullPath));
                _selectedPhotoPath = fullPath;
            }
            catch (Exception ex)
            {
                // A missing photo must not prevent the rest of the record from loading.
                ErrorLog.Write(ex, "LoadClientPhoto");
            }
        }

        // ===================================================================== helpers

        private Client BuildDraftClient()
        {
            return new Client
            {
                Id = _editingClientId,
                FullName = txtFullName.Text.Trim(),
                Address = txtAddress.Text.Trim(),
                Phone = txtPhone.Text.Trim(),
                Email = txtEmail.Text.Trim(),
                DOB = dtpDOB.Checked ? dtpDOB.Value.Date : null,
                NIC = txtNIC.Text.Trim(),
                PhotoPath = string.Empty
            };
        }

        private List<Education> CollectEducation(out List<string> errors)
        {
            var rows = new List<Education>();
            errors = new List<string>();

            foreach (DataGridViewRow row in dgvEducation.Rows)
            {
                if (row.IsNewRow) continue;

                string qualification = CellText(row, "Qualification");
                string institute = CellText(row, "Institute");
                string year = CellText(row, "Year");
                string results = CellText(row, "Results");

                bool any = new[] { qualification, institute, year, results }.Any(v => v.Length > 0);
                if (!any) continue;

                if (qualification.Length == 0)
                {
                    errors.Add($"Education row {row.Index + 1}: Qualification is required.");
                    continue;
                }

                rows.Add(new Education
                {
                    ClientId = _editingClientId,
                    Qualification = qualification,
                    Institute = institute,
                    Year = year,
                    Results = results
                });
            }

            return rows;
        }

        private List<Experience> CollectExperience(out List<string> errors)
        {
            var rows = new List<Experience>();
            errors = new List<string>();

            foreach (DataGridViewRow row in dgvExperience.Rows)
            {
                if (row.IsNewRow) continue;

                string jobTitle = CellText(row, "JobTitle");
                string company = CellText(row, "Company");
                string duration = CellText(row, "Duration");

                bool any = new[] { jobTitle, company, duration }.Any(v => v.Length > 0);
                if (!any) continue;

                if (jobTitle.Length == 0)
                {
                    errors.Add($"Experience row {row.Index + 1}: Job Title is required.");
                    continue;
                }

                rows.Add(new Experience
                {
                    ClientId = _editingClientId,
                    JobTitle = jobTitle,
                    Company = company,
                    Duration = duration
                });
            }

            return rows;
        }

        private static string CellText(DataGridViewRow row, string columnName)
        {
            // Resolve through the owning grid - DataGridViewCellCollection only has an
            // integer/Cell overload of Contains, not a string one.
            DataGridViewColumn? column = row.DataGridView?.Columns[columnName];
            if (column is null) return string.Empty;

            object? value = row.Cells[column.Index].Value;
            return value?.ToString()?.Trim() ?? string.Empty;
        }

        private int GetSelectedClientId()
        {
            if (dgvClients.SelectedRows.Count == 0) return 0;

            object? raw = dgvClients.SelectedRows[0].Cells["Id"].Value;
            if (raw is null || raw is DBNull) return 0;

            return int.TryParse(raw.ToString(), out int id) ? id : 0;
        }

        private void SelectClientRow(int clientId)
        {
            _loading = true;
            try
            {
                dgvClients.ClearSelection();
                if (SelectClientRowInternal(clientId))
                    dgvClients.FirstDisplayedScrollingRowIndex =
                        dgvClients.SelectedRows[0].Index;
            }
            finally
            {
                _loading = false;
            }

            // Load the record for real, now that programmatic selection is allowed.
            if (clientId > 0 && clientId != _editingClientId)
                LoadClientData(clientId);
        }

        private bool SelectClientRowInternal(int clientId)
        {
            foreach (DataGridViewRow row in dgvClients.Rows)
            {
                if (row.IsNewRow) continue;
                if (row.Cells["Id"].Value?.ToString() == clientId.ToString())
                {
                    row.Selected = true;
                    if (row.Cells.Count > 1) dgvClients.CurrentCell = row.Cells[1];
                    return true;
                }
            }
            return false;
        }

        private bool ConfirmDiscardChanges()
        {
            if (!_dirty) return true;

            return MessageBox.Show(this,
                "You have unsaved changes.\n\nDiscard them?",
                "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ShowValidationErrors(IReadOnlyList<string> errors)
        {
            string body = errors.Count == 1
                ? errors[0]
                : "Please fix the following:\n\n- " + string.Join("\n- ", errors);

            MessageBox.Show(this, body, "Missing or invalid information",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SetPhotoImage(Image? image)
        {
            Image? old = pbPhoto.Image;
            pbPhoto.Image = image;   // never throws: null is allowed
            if (old != null && !ReferenceEquals(old, image)) old.Dispose();
        }

        /// <summary>
        /// Reads the file into memory first. Image.FromFile would lock the file for the
        /// lifetime of the Image (and we swap images often), which would also break the
        /// later copy into the ProfilePics folder.
        /// </summary>
        private static Image LoadImageWithoutLock(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes, writable: false);
            using var temp = Image.FromStream(stream);
            return new Bitmap(temp);   // detached copy - safe to dispose the stream
        }

        /// <summary>Runs an event handler and shows/logs any unexpected failure.</summary>
        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, action.Method.Name);
                MessageBox.Show(this,
                    "Something went wrong while performing this action.\n\nDetails: " + ex.Message,
                    "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
