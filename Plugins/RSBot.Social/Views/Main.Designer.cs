namespace RSBot.Social.Views
{
    partial class Main
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            tabMain = new SDUI.Controls.TabControl();
            tabPlayers = new System.Windows.Forms.TabPage();
            tabGuild = new System.Windows.Forms.TabPage();
            tabExchange = new System.Windows.Forms.TabPage();
            tabMain.SuspendLayout();
            SuspendLayout();
            //
            // tabMain
            //
            tabMain.Controls.Add(tabPlayers);
            tabMain.Controls.Add(tabGuild);
            tabMain.Controls.Add(tabExchange);
            tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
            tabMain.ItemSize = new System.Drawing.Size(80, 24);
            tabMain.Location = new System.Drawing.Point(0, 0);
            tabMain.Margin = new System.Windows.Forms.Padding(4);
            tabMain.Name = "tabMain";
            tabMain.Radius = new System.Windows.Forms.Padding(4);
            tabMain.SelectedIndex = 0;
            tabMain.Size = new System.Drawing.Size(719, 459);
            tabMain.TabIndex = 0;
            //
            // tabPlayers
            //
            tabPlayers.BackColor = System.Drawing.Color.White;
            tabPlayers.Location = new System.Drawing.Point(4, 28);
            tabPlayers.Name = "tabPlayers";
            tabPlayers.Size = new System.Drawing.Size(711, 427);
            tabPlayers.TabIndex = 0;
            tabPlayers.Text = "Players";
            //
            // tabGuild
            //
            tabGuild.BackColor = System.Drawing.Color.White;
            tabGuild.Location = new System.Drawing.Point(4, 28);
            tabGuild.Name = "tabGuild";
            tabGuild.Size = new System.Drawing.Size(711, 427);
            tabGuild.TabIndex = 1;
            tabGuild.Text = "Guild";
            //
            // tabExchange
            //
            tabExchange.BackColor = System.Drawing.Color.White;
            tabExchange.Location = new System.Drawing.Point(4, 28);
            tabExchange.Name = "tabExchange";
            tabExchange.Size = new System.Drawing.Size(711, 427);
            tabExchange.TabIndex = 2;
            tabExchange.Text = "Exchange";
            //
            // Main
            //
            Controls.Add(tabMain);
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            Name = "Main";
            Size = new System.Drawing.Size(719, 459);
            tabMain.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private SDUI.Controls.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPlayers;
        private System.Windows.Forms.TabPage tabGuild;
        private System.Windows.Forms.TabPage tabExchange;
    }
}
