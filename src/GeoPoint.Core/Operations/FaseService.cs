using System.Text.Json;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Operations;

/// <summary>Criação, ativação e exclusão de fases do projeto.</summary>
public static class FaseService
{
    /// <summary>
    /// Cria uma fase a partir dos lotes ativos da fase-base (cópias independentes) e a torna ativa.
    /// Confrontantes que apontam para lotes copiados passam a apontar para as cópias; os demais viram texto.
    /// A numeração de vértices da nova fase começa do zero.
    /// </summary>
    public static FaseRecord Create(ParcelProject p, string nome, string situacao, string? baseFaseId = null, ICollection<string>? lotIds = null)
    {
        var current = p.Fase;
        // baseFaseId vazio ("") = fase vazia, sem lotes de partida.
        var baseF = baseFaseId == "" ? null : p.FindFase(baseFaseId) ?? current;
        var f = new FaseRecord
        {
            Numero = p.Fases.Max(x => x.Numero) + 1,
            Nome = string.IsNullOrWhiteSpace(nome) ? "Nova fase" : nome.Trim(),
            Situacao = situacao?.Trim() ?? "",
            BaseFaseId = baseF?.Id ?? "",
        };
        var src = baseF is null ? new List<LotRecord>() : p.Lots.Where(l => l.FaseId == baseF.Id && l.Status == LotStatus.Ativo && (lotIds is null || lotIds.Contains(l.Id)))
            .OrderBy(l => l.Created).ToList();
        var map = new Dictionary<string, string>();
        var copies = new List<LotRecord>();
        var now = DateTime.Now;
        for (int i = 0; i < src.Count; i++)
        {
            var c = Clone(src[i]);
            c.Id = Guid.NewGuid().ToString("N");
            c.OrigemId = src[i].Id;
            c.FaseId = f.Id;
            c.Created = now.AddMilliseconds(i);   // mantém a ordem (numeração dos vértices)
            c.Modified = now;
            c.Annotations = new();
            c.Hatches = new();
            c.FilledRegionIds = new();
            c.AuxElementIds = new();
            c.PropertyLineId = "";
            if (c.IsRemanescente && string.IsNullOrWhiteSpace(c.Nome)) c.Nome = "Remanescente";
            c.IsRemanescente = false;
            c.History = new() { new HistoryEntry { Description = $"Lote da {baseF!.Label} (início da {f.Label})", Area = SafeArea(src[i]) } };
            map[src[i].Id] = c.Id;
            copies.Add(c);
        }
        foreach (var c in copies)
            foreach (var info in c.EdgeInfos.Where(i => i.Tipo == ConfrontanteTipo.Lote && i.LotId.Length > 0))
            {
                if (map.TryGetValue(info.LotId, out var nid)) info.LotId = nid;
                else
                {
                    var nl = p.FindLot(info.LotId);
                    info.Confrontante = nl is null ? info.Confrontante : p.ConfrontanteOwner(nl) is { Length: > 0 } ow ? ow : nl.DisplayName;
                    info.Tipo = ConfrontanteTipo.Texto;
                    info.LotId = "";
                }
            }
        // Edificações dos lotes copiados acompanham a nova fase.
        foreach (var b in p.Buildings.Where(b => map.ContainsKey(b.LotId)).ToList())
        {
            var nb = JsonSerializer.Deserialize<BuildingRecord>(JsonSerializer.Serialize(b, JsonOptions.Default), JsonOptions.Default)!;
            nb.Id = Guid.NewGuid().ToString("N");
            nb.FaseId = f.Id;
            nb.LotId = map[b.LotId];
            nb.Drawings = new();
            p.Buildings.Add(nb);
        }
        p.Fases.Add(f);
        p.Lots.AddRange(copies);
        p.ActiveFaseId = f.Id;
        return f;
    }

    /// <summary>Torna ativa a fase informada.</summary>
    public static void Activate(ParcelProject p, string faseId)
    {
        if (p.FindFase(faseId) is null) throw new ArgumentException("Fase não encontrada.");
        _ = p.Fase; // garante a migração antes de trocar
        p.ActiveFaseId = faseId;
    }

    /// <summary>Exclui a fase (lotes e operações dela). A Fase 0 não pode ser excluída. Devolve os lotes removidos.</summary>
    public static List<LotRecord> Delete(ParcelProject p, string faseId)
    {
        var f = p.FindFase(faseId) ?? throw new ArgumentException("Fase não encontrada.");
        if (p.Fases.Count <= 1) throw new InvalidOperationException("O projeto precisa ter pelo menos uma fase.");
        if (p.Fases.Any(x => x.BaseFaseId == f.Id))
            throw new InvalidOperationException($"Outras fases partem da {f.Label}. Exclua-as primeiro.");
        var removed = p.Lots.Where(l => l.FaseId == f.Id).ToList();
        p.Lots.RemoveAll(l => l.FaseId == f.Id);
        p.Operations.RemoveAll(o => o.FaseId == f.Id);
        p.Sheets.RemoveAll(s => s.FaseId == f.Id);
        p.Buildings.RemoveAll(b => b.FaseId == f.Id);
        p.Fases.Remove(f);
        if (p.ActiveFaseId == f.Id) p.ActiveFaseId = (p.FindFase(f.BaseFaseId) ?? p.Fases.OrderBy(x => x.Numero).First()).Id;
        return removed;
    }

    /// <summary>Muda o número de uma fase (não pode repetir o de outra fase).</summary>
    public static void Renumber(ParcelProject p, FaseRecord f, int numero)
    {
        if (numero < 0) throw new ArgumentException("O número da fase não pode ser negativo.");
        if (p.Fases.Any(x => x != f && x.Numero == numero)) throw new InvalidOperationException($"Já existe a fase {numero}.");
        f.Numero = numero;
    }

    public static LotRecord Clone(LotRecord l) =>
        JsonSerializer.Deserialize<LotRecord>(JsonSerializer.Serialize(l, JsonOptions.Default), JsonOptions.Default)!;

    private static double SafeArea(LotRecord l) { try { return l.ToRegion().Area; } catch { return 0; } }
}
