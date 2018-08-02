﻿using System;
using System.Windows.Forms;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
using System.Linq;

using Microsoft.VisualBasic;
using static Microsoft.VisualBasic.Interaction; // needed to use MsgBox, which is nicer than MessageBox.show as you can define the style using a single paramter, no idea why this isn't just in C# already

using TvDbSharper;
using TvDbSharper.Dto;

using SubtitleFetcher.Common;
using SubtitleFetcher.Common.Parsing;
using System.Net;


namespace AutoTag {
    public partial class frmMain : Form {
		public frmMain(string[] args) {
            InitializeComponent();
			if (args.Length > 1) { // if arguments provided
				AddToTable(args.Skip(1).ToArray()); // add all except first argument to table (first argument is executing file name)
				btnProcess_Click(this, new EventArgs());
			}
        }

		#region Button click handlers
		private void btnAddFile_Click(object sender, EventArgs e) {
            if (dlgAddFile.ShowDialog() == DialogResult.OK) {
				AddToTable(dlgAddFile.FileNames);
            }
			pBarProcessed.Value = 0;
        }
	
		private void btnRemove_Click(object sender, EventArgs e) {
			if (tblFiles.CurrentRow != null) {
				tblFiles.Rows.Remove(tblFiles.CurrentRow);
			}
			pBarProcessed.Value = 0;
		}

		private void btnClear_Click(object sender, EventArgs e) {
			tblFiles.Rows.Clear();
			pBarProcessed.Value = 0;
		}

		private void btnProcess_Click(object sender, EventArgs e) {
			if (tblFiles.RowCount > 0) {
				pBarProcessed.Maximum = tblFiles.RowCount;
				SetButtonState(false); // disable all buttons
				pBarProcessed.Value = 0;
				errorsEncountered = false;

				Task processFiles = Task.Run(ProcessFilesAsync);
			}
        }
		#endregion

		private bool errorsEncountered = false; // flag for if process encountered errors or not

        private async Task ProcessFilesAsync() {
			ITvDbClient tvdb = new TvDbClient();
			await tvdb.Authentication.AuthenticateAsync("TQLC3N5YDI1AQVJF");
            foreach (DataGridViewRow row in tblFiles.Rows) {
                IncrementPBarValue();

				#region Filename parsing
				EpisodeParser parser = new EpisodeParser();
				TvReleaseIdentity episodeData;

				try {
					episodeData = parser.ParseEpisodeInfo(Path.GetFileName(row.Cells[0].Value.ToString())); // Parse info from filename
				} catch (FormatException ex) {
					errorsEncountered = true;
					SetRowError(row, "Error: " + ex.Message);
					continue;
				}

				SetRowStatus(row, "Parsed file as " + episodeData);
				#endregion

				#region TVDB API searching
				TvDbResponse<SeriesSearchResult[]> seriesIdResponse;
                try {
                    seriesIdResponse = await tvdb.Search.SearchSeriesByNameAsync(episodeData.SeriesName);
                } catch (TvDbServerException ex) {
                    SetRowError(row, "Error: Cannot find series " + episodeData.SeriesName + Environment.NewLine + "(" + ex.Message + ")");
                    continue;
                }

                var series = seriesIdResponse.Data[0];

				EpisodeQuery episodeQuery = new EpisodeQuery();
				episodeQuery.AiredSeason = episodeData.Season;
				episodeQuery.AiredEpisode = episodeData.Episode; // Define query parameters

				TvDbResponse<BasicEpisode[]> episodeResponse;
				try {
					episodeResponse = await tvdb.Series.GetEpisodesAsync(series.Id, 1, episodeQuery);
				} catch (TvDbServerException ex) {
					SetRowError(row, "Error: Cannot find " + episodeData + Environment.NewLine + "(" + ex.Message + ")");
					continue;
				}

				BasicEpisode foundEpisode = episodeResponse.Data.First();

				SetRowStatus(row, "Found " + episodeData + " (" + foundEpisode.EpisodeName + ") on TheTVDB");

				ImagesQuery coverImageQuery = new ImagesQuery();
				coverImageQuery.KeyType = KeyType.Season;
				coverImageQuery.SubKey = episodeData.Season.ToString();

				TvDbResponse<TvDbSharper.Dto.Image[]> imagesResponse = null;

				if (Properties.Settings.Default.addCoverArt == true) {
					try {
						imagesResponse = await tvdb.Series.GetImagesAsync(series.Id, coverImageQuery);
					} catch (TvDbServerException ex) {
						SetRowError(row, "Error: Failed to find episode cover - " + ex.Message);
					}
				}

				string imageFilename = "";
				if (imagesResponse != null) {
					imageFilename = imagesResponse.Data.OrderByDescending(obj => obj.RatingsInfo.Average).First().FileName.Split('/').Last(); // Find highest rated image
				}
				#endregion

				#region Tag Writing
				if (Properties.Settings.Default.tagFiles == true) {
					try {
						TagLib.File file = TagLib.File.Create(row.Cells[0].Value.ToString());
						file.Tag.Album = series.SeriesName;
						file.Tag.Disc = (uint)episodeData.Season;
						file.Tag.Track = (uint)episodeData.Episode;
						file.Tag.Title = foundEpisode.EpisodeName;
						file.Tag.Comment = foundEpisode.Overview;
						file.Tag.Genres = new string[] { "TVShows" };

						if (imageFilename != "" && Properties.Settings.Default.addCoverArt == true) { // if there is an image available and cover art is enabled
							string downloadPath = Path.GetTempPath() + "\\autotag\\";
							string downloadFile = downloadPath + imageFilename;

							if (!File.Exists(downloadFile)) { // only download file if it hasn't already been downloaded
								if (!Directory.Exists(downloadPath)) {
									Directory.CreateDirectory(downloadPath); // create temp directory
								}

								try {
									using (WebClient client = new WebClient()) {
										client.DownloadFile("https://www.thetvdb.com/banners/seasons/" + imageFilename, downloadFile); // download image
									}
									file.Tag.Pictures = new TagLib.IPicture[0]; // remove current image
									file.Save();
									file.Tag.Pictures = new TagLib.Picture[] { new TagLib.Picture(downloadFile) };

								}
								catch (WebException ex) {
									SetRowError(row, "Error: Failed to download cover art - " + ex.Message);
								}
							}
							else {
								file.Tag.Pictures = new TagLib.IPicture[0];
								file.Save();
								file.Tag.Pictures = new TagLib.Picture[] { new TagLib.Picture(downloadFile) };
							}
						}
					
						file.Save();

						SetRowStatus(row, "Successfully tagged file as " + episodeData + " (" + foundEpisode.EpisodeName + ")");
					} catch (Exception ex) {
						SetRowError(row, "Error: Could not tag file - " + ex.Message);
					}
				}
				#endregion

				#region Renaming
				if (Properties.Settings.Default.renameFiles == true) {
					string newPath = Path.GetDirectoryName(row.Cells[0].Value.ToString()) + "\\" + EscapeFilename(String.Format(Properties.Settings.Default.renamePattern, series.SeriesName, episodeData.Season, episodeData.Episode.ToString("00"), foundEpisode.EpisodeName) + Path.GetExtension(row.Cells[0].Value.ToString()));

					if (row.Cells[0].Value.ToString() != newPath) {
						try {
							if (File.Exists(newPath)) {
								throw new IOException("File already exists");
							}

							File.Move(row.Cells[0].Value.ToString(), newPath);
							SetCellValue(row.Cells[0], newPath);
						}
						catch (Exception ex) {
							SetRowError(row, "Error: Could not rename file - " + ex.Message);
						}
					}
				}

				if (errorsEncountered == false) {
					SetRowColour(row, "#4CAF50");
					SetRowStatus(row, "Success - tagged as " + String.Format(Properties.Settings.Default.renamePattern, series.SeriesName, episodeData.Season, episodeData.Episode.ToString("00"), foundEpisode.EpisodeName));
				}
				#endregion
			}

			if (errorsEncountered == false) {
                Invoke(new MethodInvoker(() => MsgBox("Files successfully processed.", MsgBoxStyle.Information, "Process Complete")));
            } else {
                Invoke(new MethodInvoker(() => MsgBox("Files processed with some error(s). See the highlighted files for details.", MsgBoxStyle.Critical, "Process Complete")));
            }

            Invoke(new MethodInvoker(() => SetButtonState(true)));
        }

		#region Add to table
		private void AddToTable(string[] files) {
			foreach (String file in files) {
				if (File.GetAttributes(file).HasFlag(FileAttributes.Directory)) { // if file is actually a directory, add the all the files in the directory
					AddToTable(Directory.GetFiles(file));
				}
				else {
					AddSingleToTable(file);
				}
			}
		}

		private void AddSingleToTable(string file) {
			if (tblFiles.Rows.Cast<DataGridViewRow>().Where(row => row.Cells[0].Value.ToString() == file).Count() == 0 && new[] { ".mp4", ".m4v", ".mkv" }.Contains(Path.GetExtension(file))) { // check file is not already added and has correct extension
				tblFiles.Rows.Add(file, "Unprocessed");
			}
		}
		#endregion

		#region UI Invokers
		private void SetRowError(DataGridViewRow row, string errorMsg) {
			if (errorsEncountered == true) {
				SetCellValue(row.Cells[1], row.Cells[1].Value.ToString() + Environment.NewLine + errorMsg);
			} else {
				SetCellValue(row.Cells[1], errorMsg);
			}
			SetRowColour(row, "#E57373");
			errorsEncountered = true;
		}

		private void SetRowStatus(DataGridViewRow row, string msg) {
			if (errorsEncountered == false) {
				SetCellValue(row.Cells[1], msg);
			}
		}

        private void IncrementPBarValue() {
            Invoke(new MethodInvoker(() => pBarProcessed.Value += 1));
        }

        private void SetCellValue(DataGridViewCell cell, Object value) {
            tblFiles.Invoke(new MethodInvoker(() => cell.Value = value));
        }

        private void SetRowColour(DataGridViewRow row, string hex) {
            tblFiles.Invoke(new MethodInvoker(() => row.DefaultCellStyle.BackColor = ColorTranslator.FromHtml(hex)));
        }
		#endregion

		#region Utility functions
		private string EscapeFilename(string filename) {
			return string.Join("", filename.Split(Path.GetInvalidFileNameChars()));
		}

		private void SetButtonState(bool state) {
			btnAddFile.Enabled = state;
			btnRemove.Enabled = state;
			btnClear.Enabled = state;
			btnProcess.Enabled = state;
			MenuStrip.Enabled = state;
			AllowDrop = state;
		}
		#endregion

		#region ToolStrip
		private void addToolStripMenuItem_Click(object sender, EventArgs e) {
			btnAddFile.PerformClick();
		}


		private void exitToolStripMenuItem_Click(object sender, EventArgs e) {
			Environment.Exit(0);
		}

		private void aboutToolStripMenuItem_Click(object sender, EventArgs e) {
			Form form = new frmAbout();
			form.ShowDialog();
		}

		private void optionsToolStripMenuItem_Click(object sender, EventArgs e) {
			Form form = new frmOptions();
			form.ShowDialog();
		}
		#endregion

		#region Drag and drop
		private void frmMain_DragEnter(object sender, DragEventArgs e) {
			if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void frmMain_DragDrop(object sender, DragEventArgs e) {
			AddToTable((string[]) e.Data.GetData(DataFormats.FileDrop));
		}
		#endregion

		private void frmMain_FormClosing(object sender, FormClosingEventArgs e) {
			if(Directory.Exists(Path.GetTempPath() + "\\autotag\\")) {
				foreach(FileInfo file in new DirectoryInfo(Path.GetTempPath() + "\\autotag\\").GetFiles()) {
					file.Delete(); // clean up temporary files
				}
			}
		}
	}
}