﻿using Entities.Models.Renta20;
using SJew.Entities.Models.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SJew.Business
{
    public class ReportService
    {
        private List<Transaction> _transactions;
        private List<Asset> _portfolio;

        private List<Transacción> _transacciones;

        public ReportService(List<Transaction> transactions, List<Asset> portfolio)
        {
            _transactions = transactions;
            _portfolio = portfolio;

            _transacciones = transactions.Select(x => new Transacción()
            {
                Id = x.Id,
                Date = x.Date,
                Product = x.Product,
                ISIN = x.ISIN,
                ExchangeMarket = x.ExchangeMarket,
                ExecutionCenter = x.ExecutionCenter,
                Quantity = x.Quantity,
                Price = x.Price,
                LocalValue = x.LocalValue,
                Value = x.Value,
                ForeignExchangeRate = x.ForeignExchangeRate,
                Charge = x.Charge,
                Total = x.Total
            }).ToList();
        }

        public Dictionary<string, List<Transmisión>> Renta20()
        {
            Dictionary<string, List<Transacción>> transaccionesPorProducto = _transacciones
                .GroupBy(x => x.Product)
                .ToDictionary(x => x.Key, x => x.ToList());

            Dictionary<string, List<Transmisión>> transmisionesPorProducto = new Dictionary<string, List<Transmisión>>();

            foreach (KeyValuePair<string, List<Transacción>> grupoProducto in transaccionesPorProducto)
            {
                List<Transmisión> transmisiones = ObtenerTransmisionesProducto(grupoProducto.Value);
                List<Transmisión> transmisionesSimplificadas = SimplificarTransmisionesProducto(transmisiones);

                transmisionesPorProducto.Add(grupoProducto.Key, transmisionesSimplificadas);
            }

            return transmisionesPorProducto;
        }

        public List<Transmisión> SimplificarTransmisionesProducto(List<Transmisión> transmisiones)
        {
            List<Transmisión> transmisionesSimplificadas = new List<Transmisión>();
            transmisionesSimplificadas.AddRange(transmisiones);

            var transmisionesSimplificables = transmisiones
                .Where(x => transmisiones
                    .Except(new List<Transmisión>() { x })
                    .Where(y => y.FechaAdquisición.Date == x.FechaAdquisición.Date && y.FechaTransmisión.Date == x.FechaTransmisión.Date)
                    .Any())
                .ToList()
                .GroupBy(x => x.FechaAdquisición.Date);

            
            foreach (Transmisión transmisiónSimplificable in transmisionesSimplificables.SelectMany(x => x))
            {
                transmisionesSimplificadas.Remove(transmisiónSimplificable);
            }

            foreach (IGrouping<DateTime, Transmisión> grupoTransmisionesDía in transmisionesSimplificables)
            {
                List<Transmisión> transmisionesDía = grupoTransmisionesDía.ToList();

                List<Transmisión> largos = transmisionesDía.Where(x => x.TipoTransmisión == TipoTransmisión.Largo).ToList();
                List<Transmisión> cortos = transmisionesDía.Where(x => x.TipoTransmisión == TipoTransmisión.Corto).ToList();

                foreach (List<Transmisión> grupoSimplificable in new List<Transmisión>[] { largos, cortos })
                {
                    if (grupoSimplificable.Any())
                    {
                        transmisionesSimplificadas.Add(AgruparTransmisiones(grupoSimplificable));
                    }
                }               
               
            }

            return transmisionesSimplificadas;
        }

        private Transmisión AgruparTransmisiones(List<Transmisión> transmisiones)
        { 
            return new Transmisión()
            {
                TipoTransmisión = transmisiones.First().TipoTransmisión,
                Producto = transmisiones.First().Producto,
                FechaAdquisición = transmisiones.Min(x => x.FechaAdquisición),
                FechaTransmisión = transmisiones.Max(x => x.FechaTransmisión),
                NúmeroTítulos = transmisiones.Sum(x => x.NúmeroTítulos),
                ValorAdquisición = transmisiones.Sum(x => x.ValorAdquisición),
                ValorAdquisiciónTotal = transmisiones.Sum(x => x.ValorAdquisiciónTotal),
                ValorTransmisión = transmisiones.Sum(x => x.ValorTransmisión),
                ValorTransmisiónTotal = transmisiones.Sum(x => x.ValorTransmisiónTotal),
                ValorComisiones = transmisiones.Sum(x => x.ValorComisiones)
            };
        }

        private List<Transmisión> ObtenerTransmisionesProducto(List<Transacción> transacciones)
        {
            List<Transmisión> transmisiones = new List<Transmisión>();

            List<Transacción> transaccionesPorFecha = transacciones.OrderBy(x => x.Date).ToList();

            int títulosPendientesDeCierre = 0;
            foreach (Transacción transacción in transaccionesPorFecha)
            {
                if (transacción.TipoTransacción == null)
                {
                    if (Math.Abs(títulosPendientesDeCierre + transacción.Quantity) > Math.Abs(títulosPendientesDeCierre))
                    {
                        //Continúa siendo apertura
                        transacción.TipoTransacción = TipoTransacción.Apertura;
                    }
                    else
                    {
                        transacción.TipoTransacción = TipoTransacción.Cierre;
                    }
                }

                if (transacción.TipoTransacción == TipoTransacción.Cierre)
                {
                    continue;
                }              

                títulosPendientesDeCierre += transacción.Quantity;

                IEnumerable<Transacción> potencialesCierres = transaccionesPorFecha.Where(x => x.TipoOperación != transacción.TipoOperación && x.CierresDisponibles > 0 && x.TipoTransacción != TipoTransacción.Apertura);

                foreach (Transacción cierre in potencialesCierres)
                {
                    if (títulosPendientesDeCierre == 0)
                    {
                        break;
                    }

                    cierre.TipoTransacción = TipoTransacción.Cierre;

                    if (Math.Abs(títulosPendientesDeCierre) >= cierre.CierresDisponibles)
                    {
                        //Uso de todos los cierres disponibles
                        int títulosCerrados = cierre.CierresDisponibles;
                        títulosPendientesDeCierre += (títulosCerrados * Math.Sign(cierre.Quantity));
                        cierre.CierresConsolidados += títulosCerrados;

                        transmisiones.Add(CrearTransmisión(transacción, cierre, títulosCerrados));
                        continue;
                    }
                    else
                    {
                        int títulosCerrados = Math.Abs(títulosPendientesDeCierre);
                        cierre.CierresConsolidados += títulosCerrados;
                        títulosPendientesDeCierre = 0;

                        transmisiones.Add(CrearTransmisión(transacción, cierre, títulosCerrados));

                        break;
                    }
                }

                if (títulosPendientesDeCierre > 0)
                {
                    transacción.TítulosSinCierre = títulosPendientesDeCierre;
                }
            }

            return transmisiones;
        }

        private Transmisión CrearTransmisión(Transacción apertura, Transacción cierre, int títulosCerrados)
        {
            string producto = apertura.Product;

            double valorComisionesApertura = ((apertura.Charge.Ammount ?? 0) / Math.Abs(apertura.Quantity)) * títulosCerrados;
            double valorComisionesCierre = ((cierre.Charge.Ammount ?? 0) / Math.Abs(cierre.Quantity)) * títulosCerrados;

            return new Transmisión()
            {
                Producto = producto,
                FechaAdquisición = apertura.Date,
                FechaTransmisión = cierre.Date,
                ValorAdquisición = (apertura.Value.Ammount.Value / Math.Abs(apertura.Quantity)) * títulosCerrados,
                ValorTransmisión = (cierre.Value.Ammount.Value / Math.Abs(cierre.Quantity)) * títulosCerrados,
                ValorAdquisiciónTotal = (apertura.Total.Ammount.Value / Math.Abs(apertura.Quantity)) * títulosCerrados,
                ValorTransmisiónTotal = (cierre.Total.Ammount.Value / Math.Abs(cierre.Quantity)) * títulosCerrados,
                ValorComisiones = valorComisionesApertura + valorComisionesCierre,
                NúmeroTítulos = títulosCerrados,
                TipoTransmisión = (apertura.TipoOperación == TipoOperación.Compra) ? TipoTransmisión.Largo : TipoTransmisión.Corto
            };
        }

        public string ReporteGlobales(Dictionary<string, List<Transmisión>> transmisionesPorProducto)
        {
            StringBuilder reporte = new StringBuilder();

            double beneficio = transmisionesPorProducto.Values.SelectMany(x => x).Where(x => x.Beneficio > 0).Sum(x => x.Beneficio);
            double pérdida = transmisionesPorProducto.Values.SelectMany(x => x).Where(x => x.Beneficio < 0).Sum(x => x.Beneficio);
            double beneficioTotal = transmisionesPorProducto.Values.SelectMany(x => x).Where(x => x.BeneficioTotal > 0).Sum(x => x.BeneficioTotal);
            double pérdidaTotal = transmisionesPorProducto.Values.SelectMany(x => x).Where(x => x.BeneficioTotal < 0).Sum(x => x.BeneficioTotal);
            double valorComisiones = transmisionesPorProducto.Values.SelectMany(x => x).Sum(x => x.ValorComisiones);
            double granTotal = transmisionesPorProducto.Values.SelectMany(x => x).Sum(x => x.BeneficioTotal);


            reporte.AppendLine(string.Format("Valor comisiones: {0}", valorComisiones));
            reporte.AppendLine(string.Format("Beneficio: {0}", beneficio));
            reporte.AppendLine(string.Format("Beneficio total: {0}", beneficioTotal));
            reporte.AppendLine(string.Format("Pérdida: {0}", pérdida));
            reporte.AppendLine(string.Format("Pérdida total: {0}", pérdidaTotal));
            reporte.AppendLine(string.Format("Diferencia: {0}", beneficio + pérdida));
            reporte.AppendLine(string.Format("Diferencia totales: {0}", beneficioTotal + pérdidaTotal));
            reporte.AppendLine(string.Format("Comisiones (estimado por diferencia): {0}", (beneficio + pérdida) - (beneficioTotal + pérdidaTotal)));
            reporte.AppendLine(string.Format("Número de transmisiones: {0}", transmisionesPorProducto.Values.SelectMany(x => x).Count()));
            reporte.AppendLine(string.Format("Gran total: {0}", granTotal));

            return reporte.ToString();
        }
    }
}
