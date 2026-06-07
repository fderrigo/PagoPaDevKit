namespace PagoPaDevKit.Mock.Gpd;

// =====================================================================
// Modello FdR (Flussi di Rendicontazione) - pagopa-fdr.
// Il PSP crea un flusso, aggiunge i pagamenti e lo pubblica; l'Ente
// (organization) lo recupera. Alla pubblicazione le rate corrispondenti
// su GPD passano a REPORTED.
// =====================================================================

public static class FdrStatus
{
    public const string Created = "CREATED";
    public const string Published = "PUBLISHED";
}

public static class FdrPayStatus
{
    public const string Executed = "EXECUTED";
    public const string Revoked = "REVOKED";
    public const string StandIn = "STAND_IN";
}

// ---------------------- Entità ----------------------
public class Fdr
{
    public int Id { get; set; }
    public string Name { get; set; } = "";          // identificativo flusso (campo "fdr")
    public string PspId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public int Revision { get; set; } = 1;
    public bool Published { get; set; }
    public string Status { get; set; } = FdrStatus.Created;
    public DateTimeOffset FdrDate { get; set; }
    public string? Regulation { get; set; }
    public DateTimeOffset? RegulationDate { get; set; }
    public string? BicCodePouringBank { get; set; }
    // Sender
    public string? SenderType { get; set; }
    public string? SenderId { get; set; }
    public string? PspName { get; set; }
    public string? PspBrokerId { get; set; }
    public string? ChannelId { get; set; }
    // Receiver
    public string? ReceiverId { get; set; }
    public string? OrganizationName { get; set; }
    public long TotPayments { get; set; }
    public decimal SumPayments { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
    public List<FdrPayment> Payments { get; set; } = new();
}

public class FdrPayment
{
    public int Id { get; set; }
    public int FdrId { get; set; }
    public long Index { get; set; }
    public string Iuv { get; set; } = "";
    public string Iur { get; set; } = "";
    public int IdTransfer { get; set; }
    public decimal Pay { get; set; }
    public string PayStatus { get; set; } = FdrPayStatus.Executed;
    public DateTimeOffset PayDate { get; set; }
}

// ---------------------- DTO PSP (publish API) ----------------------
public class CreateFlowRequest
{
    public string Fdr { get; set; } = "";
    public DateTimeOffset FdrDate { get; set; }
    public SenderDto Sender { get; set; } = new();
    public ReceiverDto Receiver { get; set; } = new();
    public string? Regulation { get; set; }
    public DateTimeOffset? RegulationDate { get; set; }
    public string? BicCodePouringBank { get; set; }
    public long TotPayments { get; set; }
    public decimal SumPayments { get; set; }
}

public class SenderDto
{
    public string? Type { get; set; }
    public string? Id { get; set; }
    public string? PspId { get; set; }
    public string? PspName { get; set; }
    public string? PspBrokerId { get; set; }
    public string? ChannelId { get; set; }
}

public class ReceiverDto
{
    public string? Id { get; set; }
    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
}

public class AddPaymentRequest { public List<PaymentDto> Payments { get; set; } = new(); }
public class DeletePaymentRequest { public List<long> Indexes { get; set; } = new(); }

public class PaymentDto
{
    public long Index { get; set; }
    public string Iuv { get; set; } = "";
    public string Iur { get; set; } = "";
    public int IdTransfer { get; set; }
    public decimal Pay { get; set; }
    public string PayStatus { get; set; } = FdrPayStatus.Executed;
    public DateTimeOffset PayDate { get; set; }
}

// ---------------------- DTO Org (pull API) ----------------------
public class PaginatedFlowsResponse
{
    public MetadataDtoFdr Metadata { get; set; } = new();
    public long Count { get; set; }
    public List<FlowSummary> Data { get; set; } = new();
}

public class MetadataDtoFdr { public int PageSize { get; set; } public int PageNumber { get; set; } public int TotPage { get; set; } }

public class FlowSummary
{
    public string Fdr { get; set; } = "";
    public string PspId { get; set; } = "";
    public int Revision { get; set; }
    public bool Published { get; set; }
    public DateTimeOffset FlowDate { get; set; }
}

public class SingleFlowResponse
{
    public string Status { get; set; } = "";
    public int Revision { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
    public string Fdr { get; set; } = "";
    public DateTimeOffset FdrDate { get; set; }
    public string? Regulation { get; set; }
    public DateTimeOffset? RegulationDate { get; set; }
    public string? BicCodePouringBank { get; set; }
    public SenderDto Sender { get; set; } = new();
    public ReceiverDto Receiver { get; set; } = new();
    public bool Published { get; set; }
    public long ComputedTotPayments { get; set; }
    public decimal ComputedSumPayments { get; set; }
    public long TotPayments { get; set; }
    public decimal SumPayments { get; set; }
}

public class PaginatedPaymentsResponse
{
    public MetadataDtoFdr Metadata { get; set; } = new();
    public long Count { get; set; }
    public List<PaymentDto> Data { get; set; } = new();
}
