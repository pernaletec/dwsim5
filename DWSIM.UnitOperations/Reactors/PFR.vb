'    PFR Calculation Routines 
'    Copyright 2008-2016 Daniel Wagner O. de Medeiros
'
'    This file is part of DWSIM.
'
'    DWSIM is free software: you can redistribute it and/or modify
'    it under the terms of the GNU General Public License as published by
'    the Free Software Foundation, either version 3 of the License, or
'    (at your option) any later version.
'
'    DWSIM is distributed in the hope that it will be useful,
'    but WITHOUT ANY WARRANTY; without even the implied warranty of
'    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
'    GNU General Public License for more details.
'
'    You should have received a copy of the GNU General Public License
'    along with DWSIM.  If not, see <http://www.gnu.org/licenses/>.


Imports DWSIM.Thermodynamics.BaseClasses
Imports Ciloci.Flee
Imports System.Math
Imports System.Linq
Imports DWSIM.Interfaces.Enums
Imports DWSIM.SharedClasses
Imports DWSIM.Thermodynamics.Streams
Imports DWSIM.Thermodynamics
Imports DWSIM.MathOps
Imports OxyPlot
Imports OxyPlot.Axes

Namespace Reactors

    <System.Serializable()> Public Class Reactor_PFR

        Inherits Reactor

        Private _IObj As InspectorItem

        Protected m_vol As Double
        Protected m_dv As Double = 0.01

        Dim C0 As Dictionary(Of String, Double)
        Dim C As Dictionary(Of String, Double)

        Dim Ri As Dictionary(Of String, Double)

        Dim Kf, Kr As ArrayList

        Dim N00 As Dictionary(Of String, Double)

        Public Rxi As New Dictionary(Of String, Double)
        Public RxiT As New Dictionary(Of String, Double)
        Public DHRi As New Dictionary(Of String, Double)

        Public points As ArrayList

        Dim activeAL As Integer = 0

        <System.NonSerialized()> Dim ims As MaterialStream

        <NonSerialized> <Xml.Serialization.XmlIgnore> Public f As EditingForm_ReactorPFR

        Private VolumeFraction As Double = 1.0#

        Public Property Length As Double = 1.0#

        Public Property CalcStep As Integer = 0

        Public Property Volume() As Double
            Get
                Return m_vol
            End Get
            Set(ByVal value As Double)
                m_vol = value
            End Set
        End Property

        Public Property dV() As Double
            Get
                Return m_dv
            End Get
            Set(ByVal value As Double)
                m_dv = value
            End Set
        End Property

        Public Property CatalystLoading As Double = 0.0#

        Public Property CatalystVoidFraction As Double = 0.0#

        Public Property CatalystParticleDiameter As Double = 0.0#

        Public Property ResidenceTime As Double = 0.0#

        Public Sub New()

            MyBase.New()

        End Sub

        Public Sub New(ByVal name As String, ByVal description As String)

            MyBase.New()
            Me.ComponentName = name
            Me.ComponentDescription = description

            N00 = New Dictionary(Of String, Double)
            C0 = New Dictionary(Of String, Double)
            C = New Dictionary(Of String, Double)
            Ri = New Dictionary(Of String, Double)
            DHRi = New Dictionary(Of String, Double)
            Kf = New ArrayList
            Kr = New ArrayList
            Rxi = New Dictionary(Of String, Double)

        End Sub

        Public Overrides Function CloneXML() As Object
            Dim obj As ICustomXMLSerialization = New Reactor_PFR()
            obj.LoadData(Me.SaveData)
            Return obj
        End Function

        Public Overrides Function CloneJSON() As Object
            Return Newtonsoft.Json.JsonConvert.DeserializeObject(Of Reactor_PFR)(Newtonsoft.Json.JsonConvert.SerializeObject(Me))
        End Function

        Public Function ODEFunc(ByVal x As Double, ByVal y As Double()) As Double()

            _IObj?.SetCurrent

            Dim IObj2 As Inspector.InspectorItem = Inspector.Host.GetNewInspectorItem()

            Inspector.Host.CheckAndAdd(IObj2, "", "ODEFunc", "ODE solver for reactor concentrations", "", True)

            IObj2?.SetCurrent

            IObj2?.Paragraphs.Add("<h2>Input Vars</h2>")

            IObj2?.Paragraphs.Add(String.Format("Volume Step: {0}", x))
            IObj2?.Paragraphs.Add(String.Format("Compound Mole Flows: {0} mol/s", y.ToMathArrayString))

            IObj2?.Paragraphs.Add("<h2>Intermediate Calcs</h2>")

            Dim conv As New SystemsOfUnits.Converter

            Dim i As Integer = 0
            Dim j As Integer = 0
            Dim scBC As Double = 0
            Dim BC As String = ""

            Dim Qf As Double

            If Me.Reactions.Count > 0 Then
                Select Case FlowSheet.Reactions(Me.Reactions(0)).ReactionPhase
                    Case PhaseName.Vapor
                        Qf = ims.Phases(2).Properties.volumetric_flow.GetValueOrDefault()
                    Case PhaseName.Liquid
                        Qf = ims.Phases(3).Properties.volumetric_flow.GetValueOrDefault()
                    Case PhaseName.Mixture
                        Qf = ims.Phases(3).Properties.volumetric_flow.GetValueOrDefault() +
                                ims.Phases(2).Properties.volumetric_flow.GetValueOrDefault()
                End Select
            End If

            j = 0
            For Each s As String In N00.Keys
                If y(j) < 0 Then
                    C(s) = 0.0#
                Else
                    C(s) = y(j) / Qf
                End If
                j = j + 1
            Next

            IObj2?.Paragraphs.Add(String.Format("Compound Concentrations: {0} mol/m3", C.Values.ToArray.ToMathArrayString))

            'conversion factors for different basis other than molar concentrations
            Dim convfactors As New Dictionary(Of String, Double)

            'loop through reactions
            Dim rxn As Reaction
            Dim ar = Me.ReactionsSequence(activeAL)

            i = 0
            Do
                'process reaction i
                rxn = FlowSheet.Reactions(ar(i))
                For Each sb As ReactionStoichBase In rxn.Components.Values
                    Ri(sb.CompName) = 0
                Next
                i += 1
            Loop Until i = ar.Count

            i = 0
            Do

                'process reaction i
                rxn = FlowSheet.Reactions(ar(i))
                BC = rxn.BaseReactant
                scBC = rxn.Components(BC).StoichCoeff

                IObj2?.Paragraphs.Add(String.Format("Reaction ID: {0}", rxn.Name))

                Dim T As Double = ims.Phases(0).Properties.temperature.GetValueOrDefault

                IObj2?.Paragraphs.Add(String.Format("T: {0} K", T))

                Dim rx As Double = 0.0#

                convfactors = Me.GetConvFactors(rxn, ims)

                Dim cvar As Double

                If rxn.ReactionType = ReactionType.Kinetic Then

                    Dim kxf As Double = rxn.A_Forward * Exp(-rxn.E_Forward / (8.314 * T))
                    Dim kxr As Double = rxn.A_Reverse * Exp(-rxn.E_Reverse / (8.314 * T))

                    If T < rxn.Tmin Or T > rxn.Tmax Then
                        kxf = 0.0#
                        kxr = 0.0#
                    End If

                    Dim rxf As Double = 1.0#
                    Dim rxr As Double = 1.0#

                    'kinetic expression

                    For Each sb As ReactionStoichBase In rxn.Components.Values
                        cvar = C(sb.CompName) * convfactors(sb.CompName)
                        rxf *= cvar ^ sb.DirectOrder
                        rxr *= cvar ^ sb.ReverseOrder
                    Next

                    rx = kxf * rxf - kxr * rxr

                    IObj2?.Paragraphs.Add(String.Format("Reaction Rate: {0} {1}", rx, rxn.VelUnit))

                    Rxi(rxn.ID) = SystemsOfUnits.Converter.ConvertToSI(rxn.VelUnit, rx)

                    Kf(i) = kxf
                    Kr(i) = kxr

                ElseIf rxn.ReactionType = ReactionType.Heterogeneous_Catalytic Then

                    If T < rxn.Tmin Or T > rxn.Tmax Then

                        rx = 0.0

                    Else

                        Dim numval, denmval As Double

                        rxn.ExpContext = New Ciloci.Flee.ExpressionContext
                        rxn.ExpContext.Imports.AddType(GetType(System.Math))
                        rxn.ExpContext.Options.ParseCulture = Globalization.CultureInfo.InvariantCulture

                        rxn.ExpContext.Variables.Clear()
                        rxn.ExpContext.Variables.Add("T", T)

                        Dim ir As Integer = 1
                        Dim ip As Integer = 1
                        Dim ine As Integer = 1

                        For Each sb As ReactionStoichBase In rxn.Components.Values
                            cvar = C(sb.CompName) * convfactors(sb.CompName)
                            If sb.StoichCoeff < 0 Then
                                IObj2?.Paragraphs.Add(String.Format("R{0} ({1}): {2} {3}", ir.ToString, sb.CompName, cvar, rxn.ConcUnit))
                                rxn.ExpContext.Variables.Add("R" & ir.ToString, cvar)
                                ir += 1
                            ElseIf sb.StoichCoeff > 0 Then
                                IObj2?.Paragraphs.Add(String.Format("P{0} ({1}): {2} {3}", ip.ToString, sb.CompName, cvar, rxn.ConcUnit))
                                rxn.ExpContext.Variables.Add("P" & ip.ToString, cvar)
                                ip += 1
                            Else
                                IObj2?.Paragraphs.Add(String.Format("N{0} ({1}): {2} {3}", ine.ToString, sb.CompName, cvar, rxn.ConcUnit))
                                rxn.ExpContext.Variables.Add("N" & ine.ToString, cvar)
                                ine += 1
                            End If
                        Next

                        rxn.Expr = rxn.ExpContext.CompileGeneric(Of Double)(rxn.RateEquationNumerator)

                        numval = rxn.Expr.Evaluate

                        rxn.Expr = rxn.ExpContext.CompileGeneric(Of Double)(rxn.RateEquationDenominator)

                        denmval = rxn.Expr.Evaluate

                        IObj2?.Paragraphs.Add(String.Format("Numerator Expression: {0}", rxn.RateEquationNumerator))
                        IObj2?.Paragraphs.Add(String.Format("Numerator Value: {0}", numval))
                        IObj2?.Paragraphs.Add(String.Format("Denominator Expression: {0}", rxn.RateEquationDenominator))
                        IObj2?.Paragraphs.Add(String.Format("Denominator Value: {0}", denmval))

                        rx = numval / denmval

                    End If

                    IObj2?.Paragraphs.Add(String.Format("Reaction Rate: {0} {1}", rx, rxn.VelUnit))

                    Rxi(rxn.ID) = SystemsOfUnits.Converter.ConvertToSI(rxn.VelUnit, rx)

                End If

                For Each sb As ReactionStoichBase In rxn.Components.Values

                    If rxn.ReactionType = ReactionType.Kinetic Then
                        Ri(sb.CompName) += Rxi(rxn.ID) * sb.StoichCoeff / rxn.Components(BC).StoichCoeff
                    ElseIf rxn.ReactionType = ReactionType.Heterogeneous_Catalytic Then
                        Ri(sb.CompName) += Rxi(rxn.ID) * sb.StoichCoeff / rxn.Components(BC).StoichCoeff * Me.CatalystLoading
                    End If

                Next

                i += 1

            Loop Until i = ar.Count

            Dim dy(Ri.Count - 1) As Double

            j = 0
            For Each kv As KeyValuePair(Of String, Double) In Ri
                dy(j) = -kv.Value
                j += 1
            Next

            IObj2?.Paragraphs.Add("<h2>Results</h2>")

            IObj2?.Paragraphs.Add(String.Format("Compound Mole Flow Variation: {0} mol/[m3.s]", dy.ToMathArrayString))

            IObj2?.Close()

            If Double.IsNaN(dy.Sum) Then Throw New Exception("PFR ODE solver failed to find a solution.")

            Return dy

        End Function

        Public Overrides Sub Calculate(Optional ByVal args As Object = Nothing)

            Dim IObj As Inspector.InspectorItem = Inspector.Host.GetNewInspectorItem()

            Inspector.Host.CheckAndAdd(IObj, "", "Calculate", If(GraphicObject IsNot Nothing, GraphicObject.Tag, "Temporary Object") & " (" & GetDisplayName() & ")", GetDisplayName() & " Calculation Routine", True)

            IObj?.SetCurrent()

            IObj?.Paragraphs.Add("To run a simulation of a reactor, the user needs to define the chemical reactions which will take place in the reactor.</p>
                                This Is done through the&nbsp;<span style='font-weight bold;'>Reactions Manager, </span>accessible through <span style='font-weight: bold;'>Simulation Settings &gt; Basis &gt; Open Chemical Reactions Manager</span> or <span style='font-weight: bold;'>Tools &gt; Reactions Manager</span> menus (see separate documentation).<br><br>Reactions can be of&nbsp;<span style='font-weight: bold;'>Equilibrium</span>,<span style='font-weight: bold;'>&nbsp;Conversion</span>,<span style='font-weight: bold;'>&nbsp;Kinetic</span> or&nbsp;<span style='font-weight: bold;'>Heterogeneous Catalytic</span> types. One or more reactions can be&nbsp;combined to define
                                            a&nbsp;<span style='font-weight bold;'>Reaction Set</span>. The reactors then 'see' the reactions through the reaction sets.
                                <br><br><span style ='font-weight bold; font-style: italic;'>Equilibrium</span>
                                Reactions are defined by an equilibrium constant (K). The source Of
                                Information for the equilibrium constant can be a direct gibbs energy
                                calculation, an expression defined by the user Or a constant value.
                                Equilibrium Reactions can be used in Equilibrium And Gibbs reactors.<br><br><span style='font-weight bold; font-style: italic;'>Conversion</span>
                                            Reactions are defined by the amount of a base compound which Is
                                consumed in the reaction. This amount can be a fixed value Or a
                                Function of() the system temperature. Conversion reactions are supported
                                by the Conversion reactor.<br><br><span style='font-style: italic;'>Kinetic</span> reactions are reactions defined by a kinetic expression. These reactions are supported by the PFR and CSTR reactors. <br><br><span style='font-style: italic;'>Heterogeneous Catalytic</span> reactions&nbsp;in DWSIM must obey the <span style='font-style: italic;'>Langmuir&#8211;Hinshelwood</span> 
                                            mechanism, where compounds react over a solid catalyst surface. In this 
                                model, Reaction rates are a function of catalyst amount (i.e. mol/kg 
                                cat.s). These Reactions are supported by the PFR And CStr reactors.<p>")

            N00 = New Dictionary(Of String, Double)
            C0 = New Dictionary(Of String, Double)
            C = New Dictionary(Of String, Double)
            Ri = New Dictionary(Of String, Double)
            DHRi = New Dictionary(Of String, Double)
            Kf = New ArrayList
            Kr = New ArrayList
            Rxi = New Dictionary(Of String, Double)

            Dim conv As New SystemsOfUnits.Converter
            Dim rxn As Reaction

            m_conversions = New Dictionary(Of String, Double)
            m_componentconversions = New Dictionary(Of String, Double)

            points = New ArrayList

            If Not Me.GraphicObject.InputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac16"))
            ElseIf Not Me.GraphicObject.OutputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac15"))
            ElseIf Not Me.GraphicObject.InputConnectors(1).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedeenerg17"))
            End If

            ims = DirectCast(FlowSheet.SimulationObjects(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name), MaterialStream).Clone
            ims.SetPropertyPackage(PropertyPackage)
            PropertyPackage.CurrentMaterialStream = ims
            ims.SetFlowsheet(Me.FlowSheet)
            ims.PreferredFlashAlgorithmTag = Me.PreferredFlashAlgorithmTag

            ResidenceTime = Volume / ims.Phases(0).Properties.volumetric_flow.GetValueOrDefault

            Me.Reactions.Clear()
            Me.ReactionsSequence.Clear()
            Me.Conversions.Clear()
            Me.ComponentConversions.Clear()
            Me.DeltaQ = 0.0#
            Me.DeltaT = 0.0#

            'check active reactions (kinetic and heterogeneous only) in the reaction set
            'check if there are multiple reactions on different phases (unsupported)

            Dim rxp As PhaseName = PhaseName.Mixture

            For Each rxnsb As ReactionSetBase In FlowSheet.ReactionSets(Me.ReactionSetID).Reactions.Values
                rxn = FlowSheet.Reactions(rxnsb.ReactionID)
                If Not rxn.Components.ContainsKey(rxn.BaseReactant) Then
                    Throw New Exception("No base reactant defined for reaction '" + rxn.Name + "'.")
                End If
                If rxn.ReactionType = ReactionType.Kinetic And rxnsb.IsActive Then
                    Me.Reactions.Add(rxnsb.ReactionID)
                    If rxp = PhaseName.Mixture Then rxp = rxn.ReactionPhase
                    If rxp <> rxn.ReactionPhase Then
                        Throw New Exception(FlowSheet.GetTranslatedString("MultipleReactionPhasesNotSupported"))
                    End If
                ElseIf rxn.ReactionType = ReactionType.Heterogeneous_Catalytic And rxnsb.IsActive Then
                    Me.Reactions.Add(rxnsb.ReactionID)
                    If rxp = PhaseName.Mixture Then rxp = rxn.ReactionPhase
                    If rxp <> rxn.ReactionPhase Then
                        Throw New Exception(FlowSheet.GetTranslatedString("MultipleReactionPhasesNotSupported"))
                    End If
                End If
            Next

            'order reactions
            Dim i As Integer
            i = 0
            Dim maxrank As Integer = 0
            For Each rxnsb As ReactionSetBase In FlowSheet.ReactionSets(Me.ReactionSetID).Reactions.Values
                If rxnsb.Rank > maxrank And Me.Reactions.Contains(rxnsb.ReactionID) Then maxrank = rxnsb.Rank
            Next

            'ordering of parallel reactions
            i = 0
            Dim arr As New List(Of String)
            Do
                arr = New List(Of String)
                For Each rxnsb As ReactionSetBase In FlowSheet.ReactionSets(Me.ReactionSetID).Reactions.Values
                    If rxnsb.Rank = i And Me.Reactions.Contains(rxnsb.ReactionID) Then arr.Add(rxnsb.ReactionID)
                Next
                If arr.Count > 0 Then Me.ReactionsSequence.Add(arr)
                i = i + 1
            Loop Until i = maxrank + 1

            Dim N0 As New Dictionary(Of String, Double)
            Dim N As New Dictionary(Of String, Double)
            Dim Nnr As New Dictionary(Of String, Double)
            N00.Clear()

            Dim DHr, Hr, Hr0, Hp, T, T0, P, P0, Qf, Q, W As Double
            Dim BC As String = ""
            Dim tmp As IFlashCalculationResult
            Dim maxXarr As New ArrayList

            'Reactants Enthalpy (kJ/kg * kg/s = kW) (ISOTHERMIC)
            W = ims.Phases(0).Properties.massflow.GetValueOrDefault
            Hr0 = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * W
            T0 = ims.Phases(0).Properties.temperature.GetValueOrDefault
            P0 = ims.Phases(0).Properties.pressure.GetValueOrDefault

            'conversion factors for different basis other than molar concentrations
            Dim convfactors As New Dictionary(Of String, Double)

            RxiT.Clear()
            DHRi.Clear()

            'do the calculations on each dV
            Dim currvol As Double = 0.0#
            Dim prevvol As Double = 0.0#

            Dim nloops As Integer = 1.0 / dV

            Dim counter As Integer = 0

            Dim Tant As Double

            CalcStep = 0

            Do

                IObj?.SetCurrent

                Dim IObj2 As Inspector.InspectorItem = Inspector.Host.GetNewInspectorItem()

                Inspector.Host.CheckAndAdd(IObj2, "", "Calculate", String.Format("PFR Volume Step Calculation (V = {0} m3)", currvol), "", True)

                IObj2?.SetCurrent()

                IObj2?.Paragraphs.Add(String.Format("This is the calculation routine for convergence of the compound concentrations/amounts at volume step {0}/{1} m3.", currvol, Volume))

                _IObj = IObj2

                C = New Dictionary(Of String, Double)
                C0 = New Dictionary(Of String, Double)

                Kf = New ArrayList(Me.Reactions.Count)
                Kr = New ArrayList(Me.Reactions.Count)

                T = ims.Phases(0).Properties.temperature.GetValueOrDefault
                P = ims.Phases(0).Properties.pressure.GetValueOrDefault

                Q = ims.Phases(0).Properties.volumetric_flow.GetValueOrDefault

                If Me.Reactions.Count > 0 Then
                    Select Case FlowSheet.Reactions(Me.Reactions(0)).ReactionPhase
                        Case PhaseName.Vapor
                            Qf = ims.Phases(2).Properties.volumetric_flow.GetValueOrDefault()
                        Case PhaseName.Liquid
                            Qf = ims.Phases(3).Properties.volumetric_flow.GetValueOrDefault()
                        Case PhaseName.Mixture
                            Qf = ims.Phases(3).Properties.volumetric_flow.GetValueOrDefault() +
                                ims.Phases(2).Properties.volumetric_flow.GetValueOrDefault()
                    End Select
                End If

                'Reactants Enthalpy (kJ/kg * kg/s = kW)
                Hr = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * W

                Ri.Clear()
                Rxi.Clear()

                'loop through reactions
                For Each ar In Me.ReactionsSequence

                    i = 0
                    DHr = 0

                    Do

                        'process reaction i
                        rxn = FlowSheet.Reactions(ar(i))

                        Dim m0 As Double = 0.0#
                        Dim m0nr As Double = 0.0#

                        'initial mole flows
                        For Each sb As ReactionStoichBase In rxn.Components.Values

                            Select Case rxn.ReactionPhase
                                Case PhaseName.Liquid
                                    m0 = ims.Phases(3).Compounds(sb.CompName).MolarFlow.GetValueOrDefault
                                Case PhaseName.Vapor
                                    m0 = ims.Phases(2).Compounds(sb.CompName).MolarFlow.GetValueOrDefault
                                Case PhaseName.Mixture
                                    m0 = ims.Phases(0).Compounds(sb.CompName).MolarFlow.GetValueOrDefault
                            End Select

                            If m0 = 0.0# Then m0 = 0.0000000001

                            m0nr = ims.Phases(0).Compounds(sb.CompName).MolarFlow.GetValueOrDefault - m0
                            If m0nr < 0.0# Then m0nr = 0.0

                            If Not N0.ContainsKey(sb.CompName) Then
                                N0.Add(sb.CompName, m0)
                                Nnr.Add(sb.CompName, m0nr)
                                N00.Add(sb.CompName, N0(sb.CompName))
                                N.Add(sb.CompName, N0(sb.CompName))
                                C0.Add(sb.CompName, N0(sb.CompName) / Qf)
                            Else
                                N0(sb.CompName) = m0
                                Nnr(sb.CompName) = m0nr
                                N(sb.CompName) = N0(sb.CompName)
                                C0(sb.CompName) = N0(sb.CompName) / Qf
                            End If

                        Next

                        Kf.Add(0.0#)
                        Kr.Add(0.0#)

                        i += 1

                    Loop Until i = ar.Count

                    'SOLVE ODEs

                    Me.activeAL = Me.ReactionsSequence.IndexOf(ar)

                    Dim vc(N.Count - 1), vc0(N.Count - 1) As Double
                    i = 0
                    For Each d As Double In N.Values
                        vc(i) = d
                        vc0(i) = vc(i)
                        i = i + 1
                    Next

                    'converge temperature

                    Do

                        Dim odesolver = New DotNumerics.ODE.OdeImplicitRungeKutta5()
                        odesolver.InitializeODEs(AddressOf ODEFunc, N.Count, 0.0, vc0)
                        IObj2?.SetCurrent
                        odesolver.Solve(vc0, 0.0#, 0.01 * dV * Volume, dV * Volume, Sub(x As Double, y As Double()) vc = y)

                        ODEFunc(0, vc)

                        If Double.IsNaN(vc.Sum) Then Throw New Exception(FlowSheet.GetTranslatedString("PFRMassBalanceError"))

                        C.Clear()
                        i = 0
                        For Each sb As KeyValuePair(Of String, Double) In C0
                            If vc(i) < 0.0# Then
                                Throw New Exception(FlowSheet.GetTranslatedString("PFRMassBalanceError") &
                                                String.Format(" Error details: ODE solver calculated negative molar flows at volume step {0}/{1} m3.", currvol, Volume))
                            End If
                            C(sb.Key) = vc(i) / Qf
                            i = i + 1
                        Next

                        i = 0
                        Do

                            'process reaction i
                            rxn = FlowSheet.Reactions(ar(i))

                            For Each sb As ReactionStoichBase In rxn.Components.Values

                                ''comp. conversions
                                If Not Me.ComponentConversions.ContainsKey(sb.CompName) Then
                                    Me.ComponentConversions.Add(sb.CompName, 0)
                                End If

                            Next

                            i += 1

                        Loop Until i = ar.Count

                        i = 0
                        For Each sb As String In Me.ComponentConversions.Keys
                            N(sb) = vc(i)
                            i += 1
                        Next

                        i = 0
                        DHr = 0.0#
                        Do

                            'process reaction i

                            rxn = FlowSheet.Reactions(ar(i))

                            'Heat released (or absorbed) (kJ/s = kW)

                            'DHr += rxn.ReactionHeat * (N(rxn.BaseReactant) - N0(rxn.BaseReactant)) / 1000
                            If rxn.ReactionType = ReactionType.Kinetic Then
                                DHr += rxn.ReactionHeat * (Rxi(rxn.ID)) / 1000 * dV * Volume
                            ElseIf rxn.ReactionType = ReactionType.Heterogeneous_Catalytic Then
                                DHr += rxn.ReactionHeat * (Rxi(rxn.ID)) / 1000 * CatalystLoading * dV * Volume
                            End If

                            i += 1

                        Loop Until i = ar.Count

                        'update mole flows/fractions
                        Dim Nsum As Double = 0.0#
                        'compute new mole flows
                        For Each s2 As Compound In ims.Phases(0).Compounds.Values
                            If N.ContainsKey(s2.Name) Then
                                Nsum += N(s2.Name) + Nnr(s2.Name)
                            Else
                                Nsum += s2.MolarFlow.GetValueOrDefault
                            End If
                        Next
                        For Each s2 As Compound In ims.Phases(0).Compounds.Values
                            If N.ContainsKey(s2.Name) Then
                                s2.MoleFraction = (N(s2.Name) + Nnr(s2.Name)) / Nsum
                                s2.MolarFlow = N(s2.Name) + Nnr(s2.Name)
                            Else
                                s2.MoleFraction = ims.Phases(0).Compounds(s2.Name).MolarFlow.GetValueOrDefault / Nsum
                                s2.MolarFlow = ims.Phases(0).Compounds(s2.Name).MolarFlow.GetValueOrDefault
                            End If
                        Next

                        ims.Phases(0).Properties.massflow = Nothing
                        ims.Phases(0).Properties.molarflow = Nsum

                        Dim mmm As Double = 0
                        Dim mf As Double = 0
                        For Each s3 As Compound In ims.Phases(0).Compounds.Values
                            mmm += s3.MoleFraction.GetValueOrDefault * s3.ConstantProperties.Molar_Weight
                        Next
                        For Each s3 As Compound In ims.Phases(0).Compounds.Values
                            s3.MassFraction = s3.MoleFraction.GetValueOrDefault * s3.ConstantProperties.Molar_Weight / mmm
                            s3.MassFlow = s3.MassFraction.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault
                            mf += s3.MassFlow.GetValueOrDefault
                        Next

                        'do a flash calc (calculate final temperature/enthalpy)

                        PropertyPackage.CurrentMaterialStream = ims

                        Tant = T

                        Select Case Me.ReactorOperationMode

                            Case OperationMode.Adiabatic

                                Me.DeltaQ = 0.0#

                                'Products Enthalpy (kJ/kg * kg/s = kW)

                                Hp = Hr - DHr

                                IObj?.SetCurrent()

                                tmp = Me.PropertyPackage.CalculateEquilibrium2(FlashCalculationType.PressureEnthalpy, P, Hp / W, T)
                                Dim Tout As Double = tmp.CalculatedTemperature.GetValueOrDefault

                                Me.DeltaT = Me.DeltaT.GetValueOrDefault + Tout - T

                                ims.Phases(0).Properties.temperature = Tout
                                ims.Phases(0).Properties.enthalpy = Hp / W

                                T = Tout

                                ims.SpecType = StreamSpec.Pressure_and_Enthalpy

                            Case OperationMode.Isothermic

                                ims.SpecType = StreamSpec.Temperature_and_Pressure

                            Case OperationMode.OutletTemperature

                                DeltaT = OutletTemperature - T0

                                ims.Phases(0).Properties.temperature += DeltaT * dV

                                T = ims.Phases(0).Properties.temperature.GetValueOrDefault
                                Tant = T

                                ims.SpecType = StreamSpec.Temperature_and_Pressure

                        End Select

                        IObj?.SetCurrent()
                        ims.PropertyPackage.CurrentMaterialStream = ims
                        ims.Calculate(True, True)

                    Loop Until Abs(T - Tant) < 0.01

                Next

                'add data to array
                Dim tmparr(C.Count + 2) As Double
                tmparr(0) = currvol / Volume * Length
                i = 1
                For Each d As Double In Me.C.Values
                    tmparr(i) = d
                    i = i + 1
                Next
                tmparr(i) = T
                tmparr(i + 1) = P

                Me.points.Add(tmparr)

                Dim Qvin, Qlin, eta_v, eta_l, rho_v, rho_l, tens, rho, eta, xv, xl As Double

                With ims
                    rho = .Phases(0).Properties.density.GetValueOrDefault
                    eta = .Phases(0).Properties.viscosity.GetValueOrDefault
                    Qlin = .Phases(3).Properties.volumetric_flow.GetValueOrDefault + .Phases(4).Properties.volumetric_flow.GetValueOrDefault
                    rho_l = .Phases(1).Properties.density.GetValueOrDefault
                    eta_l = .Phases(1).Properties.viscosity.GetValueOrDefault
                    tens = .Phases(0).Properties.surfaceTension.GetValueOrDefault
                    Qvin = .Phases(2).Properties.volumetric_flow.GetValueOrDefault
                    rho_v = .Phases(2).Properties.density.GetValueOrDefault
                    eta_v = .Phases(2).Properties.viscosity.GetValueOrDefault
                    xv = .Phases(2).Properties.massfraction.GetValueOrDefault
                    xl = .Phases(1).Properties.massfraction.GetValueOrDefault
                End With

                eta = eta_l * xl + eta_v * xv

                Dim diameter As Double = (4 * Me.Volume / PI / Me.Length) ^ 0.5

                If Me.CatalystLoading = 0.0# Then

                    Dim resv As Object
                    Dim fpp As New FlowPackages.BeggsBrill
                    Dim tipofluxo As String, holdup, dpf, dph, dpt As Double

                    resv = fpp.CalculateDeltaP(diameter * 0.0254, Me.dV * Me.Length, 0.0#, 0.000045, Qvin * 24 * 3600, Qlin * 24 * 3600, eta_v * 1000, eta_l * 1000, rho_v, rho_l, tens)

                    tipofluxo = resv(0)
                    holdup = resv(1)
                    dpf = resv(2)
                    dph = resv(3)
                    dpt = resv(4)

                    P -= dpf

                Else

                    'has catalyst, use Ergun equation for pressure drop in reactor beds

                    Dim vel As Double = (Qlin + Qvin) / (PI * diameter ^ 2 / 4)
                    Dim L As Double = Me.dV * Me.Length
                    Dim dp As Double = Me.CatalystParticleDiameter / 1000.0
                    Dim ev As Double = Me.CatalystVoidFraction

                    Dim pdrop As Double = 150 * eta * L / dp ^ 2 * (1 - ev) ^ 2 / ev ^ 3 * vel + 1.75 * L * rho / dp * (1 - ev) / ev ^ 3 * vel ^ 2

                    P -= pdrop

                End If

                If P < 0 Then Throw New Exception(FlowSheet.GetTranslatedString("PFRNegativePressureError"))

                ims.Phases(0).Properties.pressure = P

                prevvol = currvol
                currvol += dV * Volume

                counter += 1

                CalcStep = Convert.ToInt32(counter / nloops * 100)

                FlowSheet.CheckStatus()

            Loop Until counter > nloops

            Me.DeltaP = P0 - P

            RxiT.Clear()
            DHRi.Clear()
            DHr = 0.0#

            For Each ar In Me.ReactionsSequence

                i = 0
                Do

                    'process reaction i

                    rxn = FlowSheet.Reactions(ar(i))

                    Dim f = Rxi(rxn.ID) / Ri(rxn.BaseReactant)
                    If Double.IsNaN(f) Or Double.IsInfinity(f) Then f = 1.0#

                    RxiT.Add(rxn.ID, (N(rxn.BaseReactant) - N00(rxn.BaseReactant)) / rxn.Components(rxn.BaseReactant).StoichCoeff / 1000 * f)
                    DHRi.Add(rxn.ID, rxn.ReactionHeat * RxiT(rxn.ID))

                    i += 1

                Loop Until i = ar.Count

            Next

            If Me.ReactorOperationMode = OperationMode.Isothermic Then

                'Products Enthalpy (kJ/kg * kg/s = kW)
                Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                Me.DeltaQ = DHRi.Values.Sum + Hp - Hr0

                Me.DeltaT = 0.0#

            ElseIf Me.ReactorOperationMode = OperationMode.OutletTemperature Then

                'Products Enthalpy (kJ/kg * kg/s = kW)
                Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                'Heat (kW)
                Me.DeltaQ = DHRi.Values.Sum + Hp - Hr0

                Me.DeltaT = OutletTemperature - T0

            Else

                OutletTemperature = T

                Me.DeltaT = OutletTemperature - T0

            End If

            ' comp. conversions
            For Each sb As Compound In ims.Phases(0).Compounds.Values
                If Me.ComponentConversions.ContainsKey(sb.Name) Then
                    Me.ComponentConversions(sb.Name) = (N00(sb.Name) - N(sb.Name)) / N00(sb.Name)
                End If
            Next

            Dim ms As MaterialStream
            Dim cp As IConnectionPoint
            Dim mtotal, wtotal As Double

            cp = Me.GraphicObject.OutputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .SpecType = ims.SpecType
                    .Phases(0).Properties.massflow = ims.Phases(0).Properties.massflow.GetValueOrDefault
                    .Phases(0).Properties.massfraction = 1
                    .Phases(0).Properties.temperature = ims.Phases(0).Properties.temperature.GetValueOrDefault
                    .Phases(0).Properties.pressure = ims.Phases(0).Properties.pressure.GetValueOrDefault
                    .Phases(0).Properties.enthalpy = ims.Phases(0).Properties.enthalpy.GetValueOrDefault
                    Dim comp As BaseClasses.Compound
                    mtotal = 0
                    wtotal = 0
                    For Each comp In .Phases(0).Compounds.Values
                        mtotal += ims.Phases(0).Compounds(comp.Name).MoleFraction.GetValueOrDefault
                        wtotal += ims.Phases(0).Compounds(comp.Name).MassFraction.GetValueOrDefault
                    Next
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = ims.Phases(0).Compounds(comp.Name).MoleFraction.GetValueOrDefault / mtotal
                        comp.MassFraction = ims.Phases(0).Compounds(comp.Name).MassFraction.GetValueOrDefault / wtotal
                        comp.MassFlow = comp.MassFraction.GetValueOrDefault * .Phases(0).Properties.massflow.GetValueOrDefault
                        comp.MolarFlow = comp.MoleFraction.GetValueOrDefault * .Phases(0).Properties.molarflow.GetValueOrDefault
                    Next
                End With
            End If

            'Corrente de EnergyFlow - atualizar valor da potencia (kJ/s)
            Dim estr As Streams.EnergyStream = FlowSheet.SimulationObjects(Me.GraphicObject.InputConnectors(1).AttachedConnector.AttachedFrom.Name)
            With estr
                .EnergyFlow = Me.DeltaQ.GetValueOrDefault
                .GraphicObject.Calculated = True
            End With

            IObj?.Close()

        End Sub

        Public Overrides Sub DeCalculate()

            Dim j As Integer = 0

            Dim ms As MaterialStream
            Dim cp As IConnectionPoint

            cp = Me.GraphicObject.OutputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .Phases(0).Properties.temperature = Nothing
                    .Phases(0).Properties.pressure = Nothing
                    .Phases(0).Properties.enthalpy = Nothing
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = 0
                        comp.MassFraction = 0
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = Nothing
                    .Phases(0).Properties.massfraction = 1
                    .Phases(0).Properties.molarfraction = 1
                    .GraphicObject.Calculated = False
                End With
            End If

        End Sub

        Public Overrides Function GetPropertyValue(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Object

            Dim val0 As Object = MyBase.GetPropertyValue(prop, su)

            If Not val0 Is Nothing Then
                Return val0
            Else
                If su Is Nothing Then su = New SystemsOfUnits.SI
                Dim cv As New SystemsOfUnits.Converter
                Dim value As Double = 0
                Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

                Select Case propidx
                    Case 0
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.deltaP, Me.DeltaP.GetValueOrDefault)
                    Case 1
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.time, Me.ResidenceTime)
                    Case 2
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.volume, Me.Volume)
                    Case 3
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.distance, Me.Length)
                    Case 4
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.density, Me.CatalystLoading)
                    Case 5
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.diameter, Me.CatalystParticleDiameter)
                    Case 6
                        value = CatalystVoidFraction
                    Case 7
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.deltaT, Me.DeltaT.GetValueOrDefault)
                    Case 8
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.DeltaQ.GetValueOrDefault)
                End Select

                Return value
            End If
        End Function

        Public Overloads Overrides Function GetProperties(ByVal proptype As Interfaces.Enums.PropertyType) As String()
            Dim i As Integer = 0
            Dim proplist As New ArrayList
            Dim basecol = MyBase.GetProperties(proptype)
            If basecol.Length > 0 Then proplist.AddRange(basecol)
            Select Case proptype
                Case PropertyType.RW
                    For i = 0 To 8
                        proplist.Add("PROP_PF_" + CStr(i))
                    Next
                Case PropertyType.WR
                    For i = 0 To 8
                        proplist.Add("PROP_PF_" + CStr(i))
                    Next
                Case PropertyType.ALL
                    For i = 0 To 8
                        proplist.Add("PROP_PF_" + CStr(i))
                    Next
            End Select
            Return proplist.ToArray(GetType(System.String))
            proplist = Nothing
        End Function

        Public Overrides Function SetPropertyValue(ByVal prop As String, ByVal propval As Object, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Boolean

            If MyBase.SetPropertyValue(prop, propval, su) Then Return True

            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx
                Case 0
                    Me.DeltaP = SystemsOfUnits.Converter.ConvertToSI(su.deltaP, propval)
                Case 1
                    Me.ResidenceTime = SystemsOfUnits.Converter.ConvertToSI(su.time, propval)
                Case 2
                    Me.Volume = SystemsOfUnits.Converter.ConvertToSI(su.volume, propval)
                Case 3
                    Me.Length = SystemsOfUnits.Converter.ConvertToSI(su.distance, propval)
                Case 4
                    Me.CatalystLoading = SystemsOfUnits.Converter.ConvertToSI(su.density, propval)
                Case 5
                    Me.CatalystParticleDiameter = SystemsOfUnits.Converter.ConvertToSI(su.diameter, propval)
                Case 6
                    CatalystVoidFraction = propval
                Case 7
                    Me.DeltaT = SystemsOfUnits.Converter.ConvertToSI(su.deltaT, propval)

            End Select
            Return 1
        End Function

        Public Overrides Function GetPropertyUnit(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As String
            Dim u0 As String = MyBase.GetPropertyUnit(prop, su)

            If u0 <> "NF" Then
                Return u0
            Else
                If su Is Nothing Then su = New SystemsOfUnits.SI
                Dim cv As New SystemsOfUnits.Converter
                Dim value As String = ""
                Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

                Select Case propidx
                    Case 0
                        value = su.deltaP
                    Case 1
                        value = su.time
                    Case 2
                        value = su.volume
                    Case 3
                        value = su.distance
                    Case 4
                        value = su.density
                    Case 5
                        value = su.diameter
                    Case 6
                        value = ""
                    Case 7
                        value = su.deltaT
                    Case 8
                        value = su.heatflow
                End Select

                Return value
            End If
        End Function

        Public Overrides Sub DisplayEditForm()

            If f Is Nothing Then
                f = New EditingForm_ReactorPFR With {.SimObject = Me}
                f.ShowHint = GlobalSettings.Settings.DefaultEditFormLocation
                f.Tag = "ObjectEditor"
                Me.FlowSheet.DisplayForm(f)
            Else
                If f.IsDisposed Then
                    f = New EditingForm_ReactorPFR With {.SimObject = Me}
                    f.ShowHint = GlobalSettings.Settings.DefaultEditFormLocation
                    f.Tag = "ObjectEditor"
                    Me.FlowSheet.DisplayForm(f)
                Else
                    f.Activate()
                End If
            End If

        End Sub

        Public Overrides Sub UpdateEditForm()
            If f IsNot Nothing Then
                If Not f.IsDisposed Then
                    f.UIThread(Sub() f.UpdateInfo())
                End If
            End If
        End Sub

        Public Overrides Function GetIconBitmap() As Object
            Return My.Resources.re_pfr_32
        End Function

        Public Overrides Function GetDisplayDescription() As String
            Return ResMan.GetLocalString("PFR_Desc")
        End Function

        Public Overrides Function GetDisplayName() As String
            Return ResMan.GetLocalString("PFR_Name")
        End Function

        Public Overrides Sub CloseEditForm()
            If f IsNot Nothing Then
                If Not f.IsDisposed Then
                    f.Close()
                    f = Nothing
                End If
            End If
        End Sub

        Public Overrides ReadOnly Property MobileCompatible As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Function GetReport(su As IUnitsOfMeasure, ci As Globalization.CultureInfo, numberformat As String) As String

            Dim str As New Text.StringBuilder

            str.AppendLine("Reactor: " & Me.GraphicObject.Tag)
            str.AppendLine("Property Package: " & Me.PropertyPackage.ComponentName)
            str.AppendLine()
            str.AppendLine("Calculation Parameters")
            str.AppendLine()
            str.AppendLine("    Calculation Mode: " & ReactorOperationMode.ToString)
            str.AppendLine("    Reactor Volume: " & SystemsOfUnits.Converter.ConvertFromSI(su.volume, Me.Volume).ToString(numberformat, ci) & " " & su.volume)
            str.AppendLine("    Reactor Length: " & SystemsOfUnits.Converter.ConvertFromSI(su.distance, Me.Length).ToString(numberformat, ci) & " " & su.distance)
            str.AppendLine("    Pressure Drop: " & SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.DeltaP.GetValueOrDefault).ToString(numberformat, ci) & " " & su.deltaP)
            str.AppendLine()
            str.AppendLine("Results")
            str.AppendLine()
            Select Case Me.ReactorOperationMode
                Case OperationMode.Adiabatic
                    str.AppendLine("    Outlet Temperature: " & SystemsOfUnits.Converter.ConvertFromSI(su.temperature, Me.OutletTemperature).ToString(numberformat, ci) & " " & su.temperature)
                Case OperationMode.Isothermic
                    str.AppendLine("    Heat Added/Removed: " & SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.DeltaQ.GetValueOrDefault).ToString(numberformat, ci) & " " & su.heatflow)
                Case OperationMode.OutletTemperature
                    str.AppendLine("    Outlet Temperature: " & SystemsOfUnits.Converter.ConvertFromSI(su.temperature, Me.OutletTemperature).ToString(numberformat, ci) & " " & su.temperature)
                    str.AppendLine("    Heat Added/Removed: " & SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.DeltaQ.GetValueOrDefault).ToString(numberformat, ci) & " " & su.heatflow)
            End Select
            str.AppendLine("    Residence Time: " & SystemsOfUnits.Converter.ConvertFromSI(su.time, Me.ResidenceTime).ToString(numberformat, ci) & " " & su.time)
            str.AppendLine()
            str.AppendLine("Reaction Extents")
            str.AppendLine()
            For Each dbl As KeyValuePair(Of String, Double) In Me.RxiT
                str.AppendLine("    " & Me.GetFlowsheet.Reactions(dbl.Key).Name & ": " & SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, dbl.Value).ToString(numberformat, ci) & " " & su.molarflow)
            Next
            str.AppendLine()
            str.AppendLine("Reaction Rates")
            str.AppendLine()
            For Each dbl As KeyValuePair(Of String, Double) In Me.RxiT
                str.AppendLine("    " & Me.GetFlowsheet.Reactions(dbl.Key).Name & ": " & SystemsOfUnits.Converter.ConvertFromSI(su.reac_rate, (dbl.Value / Me.Volume)).ToString(numberformat, ci) & " " & su.reac_rate)
            Next
            str.AppendLine()
            str.AppendLine("Reaction Heats")
            str.AppendLine()
            For Each dbl As KeyValuePair(Of String, Double) In Me.DHRi
                str.AppendLine("    " & Me.GetFlowsheet.Reactions(dbl.Key).Name & ": " & SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, dbl.Value).ToString(numberformat, ci) & " " & su.heatflow)
            Next
            str.AppendLine()
            str.AppendLine("Compound Conversions")
            str.AppendLine()
            For Each dbl As KeyValuePair(Of String, Double) In Me.ComponentConversions
                If dbl.Value > 0 Then
                    str.AppendLine("    " & dbl.Key & ": " & (dbl.Value * 100).ToString(numberformat, ci) & "%")
                End If
            Next
            Return str.ToString

        End Function

        Public Overrides Function GetPropertyDescription(p As String) As String
            If p.Equals("Calculation Mode") Then
                Return "Select the calculation mode of this reactor."
            ElseIf p.Equals("Pressure Drop") Then
                Return "Enter the desired pressure drop for this reactor."
            ElseIf p.Equals("Outlet Temperature") Then
                Return "If you chose 'Outlet Temperature' as the calculation mode, enter the desired value. If you chose a different calculation mode, this parameter will be calculated."
            ElseIf p.Equals("Reactor Volume") Then
                Return "Define the active volume of this reactor."
            ElseIf p.Equals("Reactor Length") Then
                Return "Define the active length of this reactor."
            ElseIf p.Equals("Catalyst Loading") Then
                Return "Enter the amount of catalyst per unit volume (for HetCat reactions only)."
            ElseIf p.Equals("Catalyst Diameter") Then
                Return "Enter the diameter of the catalyst sphere (for HetCat reactions only)."
            ElseIf p.Equals("Catalyst Void Fraction") Then
                Return "Enter the void fraction of the catalyst bed in the reactor (for HetCat reactions only)."
            Else
                Return p
            End If
        End Function

        Public Overrides Function GetChartModel(name As String) As Object

            Dim su = FlowSheet.FlowsheetOptions.SelectedUnitSystem

            Dim model = New PlotModel() With {.Subtitle = name, .Title = GraphicObject.Tag}

            model.TitleFontSize = 11
            model.SubtitleFontSize = 10

            model.Axes.Add(New LinearAxis() With { _
                .MajorGridlineStyle = LineStyle.Dash, _
                .MinorGridlineStyle = LineStyle.Dot, _
                .Position = AxisPosition.Bottom, _
                .FontSize = 10, _
                .Title = "Length (" + su.distance + ")" _
            })

            model.LegendFontSize = 9
            model.LegendPlacement = LegendPlacement.Outside
            model.LegendOrientation = LegendOrientation.Horizontal
            model.LegendPosition = LegendPosition.BottomCenter
            model.TitleHorizontalAlignment = TitleHorizontalAlignment.CenteredWithinView

            Dim vx As New List(Of Double)(), vy As New List(Of Double)()
            Dim vya As New List(Of List(Of Double))()
            Dim vn As New List(Of String)()

            For Each obj In points
                vx.Add(DirectCast(obj, Double())(0))
            Next

            Dim j As Integer
            For j = 1 To ComponentConversions.Count + 2
                vy = New List(Of Double)()
                For Each obj In points
                    vy.Add(DirectCast(obj, Double())(j))
                Next
                vya.Add(vy)
            Next
            For Each st In ComponentConversions.Keys
                vn.Add(st)
            Next
            Dim color As OxyColor

            Select Case name

                Case "Temperature Profile"

                    model.Axes.Add(New LinearAxis() With { _
                        .MajorGridlineStyle = LineStyle.Dash, _
                        .MinorGridlineStyle = LineStyle.Dot, _
                        .Position = AxisPosition.Left, _
                        .FontSize = 10, _
                        .Title = "Temperature (" + su.temperature + ")", _
                        .Key = "temp"
                    })

                    color = OxyColor.FromRgb(Convert.ToByte(New Random().[Next](0, 255)), Convert.ToByte(New Random().[Next](0, 255)), Convert.ToByte(New Random().[Next](0, 255)))
                    model.AddLineSeries(SystemsOfUnits.Converter.ConvertArrayFromSI(su.distance, vx.ToArray()), SystemsOfUnits.Converter.ConvertArrayFromSI(su.temperature, vya(ComponentConversions.Count).ToArray()), color)
                    model.Series(model.Series.Count - 1).Title = "Temperature"
                    DirectCast(model.Series(model.Series.Count - 1), OxyPlot.Series.LineSeries).YAxisKey = "temp"

                Case "Pressure Profile"

                    model.Axes.Add(New LinearAxis() With { _
                        .MajorGridlineStyle = LineStyle.Dash, _
                        .MinorGridlineStyle = LineStyle.Dot, _
                        .Position = AxisPosition.Left, _
                        .FontSize = 10, _
                        .Title = "Pressure (" + su.pressure + ")", _
                        .Key = "press"
                    })

                    color = OxyColor.FromRgb(Convert.ToByte(New Random().[Next](0, 255)), Convert.ToByte(New Random().[Next](0, 255)), Convert.ToByte(New Random().[Next](0, 255)))
                    model.AddLineSeries(SystemsOfUnits.Converter.ConvertArrayFromSI(su.distance, vx.ToArray()), SystemsOfUnits.Converter.ConvertArrayFromSI(su.pressure, vya(ComponentConversions.Count + 1).ToArray()), color)
                    model.Series(model.Series.Count - 1).Title = "Pressure"
                    DirectCast(model.Series(model.Series.Count - 1), OxyPlot.Series.LineSeries).YAxisKey = "press"

                Case "Concentration Profile"

                    model.Axes.Add(New LinearAxis() With { _
                        .MajorGridlineStyle = LineStyle.Dash, _
                        .MinorGridlineStyle = LineStyle.Dot, _
                        .Position = AxisPosition.Left, _
                        .FontSize = 10, _
                        .Title = "Concentration (" + su.molar_conc + ")", _
                        .Key = "conc"
                    })

                    For j = 0 To vn.Count - 1
                        color = OxyColor.FromRgb(Convert.ToByte(New Random().[Next](0, 255)), Convert.ToByte(New Random().[Next](0, 255)), Convert.ToByte(New Random().[Next](0, 255)))
                        model.AddLineSeries(SystemsOfUnits.Converter.ConvertArrayFromSI(su.distance, vx.ToArray()), SystemsOfUnits.Converter.ConvertArrayFromSI(su.molar_conc, vya(j).ToArray()), color)
                        model.Series(model.Series.Count - 1).Title = vn(j)
                        DirectCast(model.Series(model.Series.Count - 1), OxyPlot.Series.LineSeries).YAxisKey = "conc"
                    Next

            End Select

            Return model

        End Function

        Public Overrides Function GetChartModelNames() As List(Of String)
            Return New List(Of String)({"Temperature Profile", "Pressure Profile", "Concentration Profile"})
        End Function

    End Class

End Namespace



