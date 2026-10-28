namespace Instancium.Vectra.Tests.Integration.Bench;

/// <summary>
/// Generates synthetic multi-topic corpus with controlled distractors.
/// Each topic has distinct vocabulary; distractors share surface structure
/// but differ in domain-specific terms.
/// </summary>
public static class SyntheticCorpus
{
    public static IReadOnlyList<SyntheticTopic> Generate()
    {
        var topics = new List<SyntheticTopic>
        {
            new("Thermocouple Seebeck Effect", 3, 4,
                "A thermocouple produces a thermoelectric voltage from two dissimilar conductors. The Seebeck effect generates voltage proportional to temperature difference between measuring and reference junctions. Type K uses nickel-based alloys for general industrial temperature measurement.",
                "thermoelectric voltage dissimilar conductors Seebeck temperature difference junctions"),
            new("RTD Platinum Resistance", 9, 10,
                "A resistance temperature detector infers temperature from platinum element resistance. Pt100 has nominal 100 ohms at zero Celsius. Four-lead measurement separates excitation and sensing to eliminate lead resistance contribution.",
                "platinum resistance Pt100 temperature detector four-lead excitation ohms"),
            new("Pressure Transmitter Diaphragm", 15, 16,
                "A pressure transmitter converts diaphragm deformation into electrical signal. Gauge measurement references atmosphere; absolute uses evacuated cavity. Strain-sensitive resistance or capacitance detects membrane displacement.",
                "pressure transmitter diaphragm gauge absolute strain capacitance deformation"),
            new("Differential Pressure Two Inputs", 21, 22,
                "A differential pressure transmitter measures difference between high-side and low-side connections. Static pressure loads both sides equally. Impulse lines communicate process pressure to sensing element.",
                "differential pressure high-side low-side static impulse connections transmitter"),
            new("Magnetic Flow Meter Induction", 27, 28,
                "A magnetic flow meter determines volumetric flow of conductive liquid through electromagnetic induction. Electrodes detect voltage induced as liquid crosses magnetic field. Lined tube prevents shorting.",
                "magnetic flow meter electromagnetic induction conductive liquid electrodes voltage"),
            new("Vortex Flow Meter Shedding", 33, 34,
                "A vortex flow meter derives flow from frequency of alternating vortices shed by obstruction. Strouhal number relates shedding frequency to velocity. Reynolds number defines qualified regime.",
                "vortex shedding frequency Strouhal obstruction alternating flow meter Reynolds"),
            new("Control Valve Positioner Travel", 39, 40,
                "A control valve modulates flow by changing internal passage via actuator. Positioner compares demanded travel with measured position. Inherent characteristic describes restriction under constant differential.",
                "control valve modulate actuator positioner travel characteristic passage"),
            new("Solenoid Valve Coil Switching", 45, 46,
                "A solenoid valve uses energized coil to create magnetic force moving closure mechanism. Direct-acting construction moves main closure; pilot-operated uses process pressure differential.",
                "solenoid valve coil magnetic force closure direct-acting pilot-operated switching"),
            new("Guided Wave Radar Reflection", 51, 52,
                "Guided wave radar determines surface position from travel time of electromagnetic energy along probe. Reflection occurs where dielectric properties change at liquid boundary.",
                "guided wave radar travel time electromagnetic probe reflection dielectric surface"),
            new("Capacitive Level Switch Threshold", 57, 58,
                "A capacitive level switch detects threshold from capacitance change around sensing electrode. Conductive liquids require insulated construction. Hysteresis prevents chatter near switching point.",
                "capacitive level switch threshold electrode capacitance insulated hysteresis"),
            new("pH Sensor Glass Membrane", 63, 64,
                "A pH sensor uses hydrogen-ion-responsive glass membrane producing Nernstian potential. Sensitivity is approximately 59 millivolts per pH unit at 25 degrees. Reference electrode provides ionic contact.",
                "pH sensor glass membrane hydrogen-ion Nernstian millivolts reference electrode"),
            new("ORP Redox Electrode Potential", 69, 70,
                "An ORP sensor measures electrode potential reflecting oxidation and reduction conditions. Platinum measuring electrode responds to mixed sample species. Mixed potential results from multiple reaction kinetics.",
                "ORP redox oxidation reduction electrode potential platinum mixed kinetics"),
            new("Piezoelectric Accelerometer Charge", 75, 76,
                "A piezoelectric accelerometer converts dynamic force on crystal into electrical charge. Seismic mass applies force during acceleration. Charge amplifier converts to voltage through feedback.",
                "piezoelectric accelerometer charge crystal seismic mass dynamic force amplifier"),
            new("Eddy Current Probe Gap", 81, 82,
                "An eddy current proximity probe determines gap to conductive target through coil impedance change. Induced currents in target alter electromagnetic response. Matched probe cable conditioner system.",
                "eddy current probe gap conductive target coil impedance induced currents"),
            new("Two-Wire Loop-Powered Transmitter", 87, 88,
                "A loop-powered two-wire transmitter receives power and communicates measurement through same conductor pair. Current represents process variable while retaining terminal voltage for electronics.",
                "loop-powered two-wire transmitter current signal power conductor pair terminal voltage"),
        };

        return topics;
    }
}

public sealed record SyntheticTopic(
    string Name,
    int PageFrom,
    int PageTo,
    string ChunkText,
    string Query)
{
    public IReadOnlyList<(string Text, int PageNumber)> Chunks =>
        new[] { (ChunkText, PageFrom) };
}