﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DWSIM.Interfaces;
using DWSIM.Thermodynamics.AdvancedEOS.Auxiliary;

namespace DWSIM.Thermodynamics.AdvancedEOS.EditingForms
{
    public partial class VPT_Editor : Form
    {

        bool Loaded = false;

        public VPTPropertyPackage PP;

        public VPT_Editor()
        {
            Application.EnableVisualStyles();
            InitializeComponent();
        }

        private void VPT_Editor_Load(object sender, EventArgs e)
        {

            Loaded = false;

            chkUseLK.Checked = PP.UseLeeKeslerEnthalpy;

            List<ICompoundConstantProperties> compounds;

            if (GlobalSettings.Settings.CAPEOPENMode)
            {
                compounds = PP._selectedcomps.Values.Select(x => (ICompoundConstantProperties)x).ToList();
            }
            else
            {
                compounds = PP.Flowsheet.SelectedCompounds.Values.ToList();
            }

            foreach (ICompoundConstantProperties cp in compounds)
            {
            gt0:
                if (PP.InteractionParameters.ContainsKey(cp.Name))
                {
                    foreach (ICompoundConstantProperties cp2 in compounds)
                    {
                        if (cp.Name != cp2.Name)
                        {
                            if (!PP.InteractionParameters[cp.Name].ContainsKey(cp2.Name))
                            {
                                //check if collection has id2 as primary id
                                if (PP.InteractionParameters.ContainsKey(cp2.Name))
                                {
                                    if (!PP.InteractionParameters[cp2.Name].ContainsKey(cp.Name))
                                    {
                                        var ip = new VPT_IP();
                                        ip.Compound1 = cp.Name;
                                        ip.Compound2 = cp2.Name;
                                        PP.InteractionParameters[cp.CAS_Number].Add(cp2.CAS_Number, ip);
                                        PP.InteractionParameters[cp.Name].Add(cp2.Name, ip);
                                        dgvkij.Rows.Add(new object[] {
								            cp.Name,
								            cp2.Name,
								            PP.InteractionParameters[cp.Name][cp2.Name].k1,
                                            PP.InteractionParameters[cp.Name][cp2.Name].k2,
                                            PP.InteractionParameters[cp.Name][cp2.Name].k3
							            });
                                    }
                                }
                            }
                            else
                            {
                                dgvkij.Rows.Add(new object[] {
						            cp.Name,
						            cp2.Name,
						            PP.InteractionParameters[cp.Name][cp2.Name].k1,
                                    PP.InteractionParameters[cp.Name][cp2.Name].k2,
                                    PP.InteractionParameters[cp.Name][cp2.Name].k3
					            });
                            }
                        }
                    }
                }
                else
                {
                    PP.InteractionParameters.Add(cp.Name, new Dictionary<string, VPT_IP>());
                    goto gt0;
                }
            }

            Loaded = true;

        }

        private void dgvkij_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (Loaded)
            {
                string id1 = dgvkij.Rows[e.RowIndex].Cells[0].Value.ToString();
                string id2 = dgvkij.Rows[e.RowIndex].Cells[1].Value.ToString();
                switch (e.ColumnIndex)
                {
                    case 2:
                        double.TryParse(dgvkij.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString(), out PP.InteractionParameters[id1][id2].k1);
                        break;
                    case 3:
                        double.TryParse(dgvkij.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString(), out PP.InteractionParameters[id1][id2].k2);
                        break;
                    case 4:
                        double.TryParse(dgvkij.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString(), out PP.InteractionParameters[id1][id2].k3);
                        break;
                }
            }


        }

        private void chkUseLK_CheckedChanged(object sender, EventArgs e)
        {
            PP.UseLeeKeslerEnthalpy = chkUseLK.Checked;
        }

    }
}
