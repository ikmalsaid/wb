using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace wb.Installer
{
    /// <summary>
    /// Robust, zero-dependency SVG path tokenizer and parser targeting standard System.Drawing.Drawing2D.GraphicsPath.
    /// Supports M, L, H, V, C, S, Q, T, A (elliptical arcs compliant with SVG standard), and Z commands.
    /// Reusable for any vector path rendering in WinForms.
    /// </summary>
    public static class SvgPathParser
    {
        public static GraphicsPath Parse(string pathData)
        {
            GraphicsPath path = new GraphicsPath();
            path.FillMode = FillMode.Winding;

            List<PathToken> tokens = Tokenize(pathData);
            int idx = 0;

            float curX = 0, curY = 0;
            float startX = 0, startY = 0;
            float lastControlX = 0, lastControlY = 0;
            char lastCmd = ' ';

            while (idx < tokens.Count)
            {
                PathToken token = tokens[idx++];
                if (token.Type != TokenType.Command) continue;
                char cmd = token.Command;

                switch (cmd)
                {
                    case 'M':
                    case 'm':
                        {
                            bool first = true;
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'm') { x += curX; y += curY; }
                                if (first)
                                {
                                    path.StartFigure();
                                    curX = x; curY = y;
                                    startX = x; startY = y;
                                    first = false;
                                }
                                else
                                {
                                    path.AddLine(curX, curY, x, y);
                                    curX = x; curY = y;
                                }
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }

                    case 'L':
                    case 'l':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'l') { x += curX; y += curY; }
                                path.AddLine(curX, curY, x, y);
                                curX = x; curY = y;
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }

                    case 'H':
                    case 'h':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x = tokens[idx++].Number;
                                if (cmd == 'h') x += curX;
                                path.AddLine(curX, curY, x, curY);
                                curX = x;
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }

                    case 'V':
                    case 'v':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float y = tokens[idx++].Number;
                                if (cmd == 'v') y += curY;
                                path.AddLine(curX, curY, curX, y);
                                curY = y;
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }

                    case 'C':
                    case 'c':
                        {
                            while (idx + 5 < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x1 = tokens[idx++].Number;
                                float y1 = tokens[idx++].Number;
                                float x2 = tokens[idx++].Number;
                                float y2 = tokens[idx++].Number;
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'c')
                                {
                                    x1 += curX; y1 += curY;
                                    x2 += curX; y2 += curY;
                                    x += curX; y += curY;
                                }
                                path.AddBezier(curX, curY, x1, y1, x2, y2, x, y);
                                lastControlX = x2; lastControlY = y2;
                                curX = x; curY = y;
                            }
                            lastCmd = cmd;
                            break;
                        }

                    case 'S':
                    case 's':
                        {
                            while (idx + 3 < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x2 = tokens[idx++].Number;
                                float y2 = tokens[idx++].Number;
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 's')
                                {
                                    x2 += curX; y2 += curY;
                                    x += curX; y += curY;
                                }
                                float x1 = curX;
                                float y1 = curY;
                                if (lastCmd == 'C' || lastCmd == 'c' || lastCmd == 'S' || lastCmd == 's')
                                {
                                    x1 = 2f * curX - lastControlX;
                                    y1 = 2f * curY - lastControlY;
                                }
                                path.AddBezier(curX, curY, x1, y1, x2, y2, x, y);
                                lastControlX = x2; lastControlY = y2;
                                curX = x; curY = y;
                            }
                            lastCmd = cmd;
                            break;
                        }

                    case 'Q':
                    case 'q':
                        {
                            while (idx + 3 < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float qx1 = tokens[idx++].Number;
                                float qy1 = tokens[idx++].Number;
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'q')
                                {
                                    qx1 += curX; qy1 += curY;
                                    x += curX; y += curY;
                                }
                                float cx1 = curX + (2f / 3f) * (qx1 - curX);
                                float cy1 = curY + (2f / 3f) * (qy1 - curY);
                                float cx2 = x + (2f / 3f) * (qx1 - x);
                                float cy2 = y + (2f / 3f) * (qy1 - y);
                                path.AddBezier(curX, curY, cx1, cy1, cx2, cy2, x, y);
                                lastControlX = qx1; lastControlY = qy1;
                                curX = x; curY = y;
                            }
                            lastCmd = cmd;
                            break;
                        }

                    case 'T':
                    case 't':
                        {
                            while (idx + 1 < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 't') { x += curX; y += curY; }
                                float qx1 = curX;
                                float qy1 = curY;
                                if (lastCmd == 'Q' || lastCmd == 'q' || lastCmd == 'T' || lastCmd == 't')
                                {
                                    qx1 = 2f * curX - lastControlX;
                                    qy1 = 2f * curY - lastControlY;
                                }
                                float cx1 = curX + (2f / 3f) * (qx1 - curX);
                                float cy1 = curY + (2f / 3f) * (qy1 - curY);
                                float cx2 = x + (2f / 3f) * (qx1 - x);
                                float cy2 = y + (2f / 3f) * (qy1 - y);
                                path.AddBezier(curX, curY, cx1, cy1, cx2, cy2, x, y);
                                lastControlX = qx1; lastControlY = qy1;
                                curX = x; curY = y;
                            }
                            lastCmd = cmd;
                            break;
                        }

                    case 'A':
                    case 'a':
                        {
                            while (idx + 6 < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float rx = tokens[idx++].Number;
                                float ry = tokens[idx++].Number;
                                float xAxisRotation = tokens[idx++].Number;
                                float largeArcFlag = tokens[idx++].Number;
                                float sweepFlag = tokens[idx++].Number;
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;

                                if (cmd == 'a') { x += curX; y += curY; }

                                ArcToCubics(path, curX, curY, rx, ry, xAxisRotation, largeArcFlag != 0, sweepFlag != 0, x, y);

                                curX = x; curY = y;
                                lastControlX = curX; lastControlY = curY;
                            }
                            lastCmd = cmd;
                            break;
                        }

                    case 'Z':
                    case 'z':
                        {
                            path.CloseFigure();
                            curX = startX; curY = startY;
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }
                }
            }

            return path;
        }

        private static void ArcToCubics(GraphicsPath path, float x1, float y1, float rx, float ry,
            float angleDeg, bool largeArc, bool sweep, float x2, float y2)
        {
            if (x1 == x2 && y1 == y2) return;

            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0)
            {
                path.AddLine(x1, y1, x2, y2);
                return;
            }

            double phi = angleDeg * Math.PI / 180.0;
            double cosPhi = Math.Cos(phi);
            double sinPhi = Math.Sin(phi);

            double dx = (x1 - x2) / 2.0;
            double dy = (y1 - y2) / 2.0;

            double x1p = cosPhi * dx + sinPhi * dy;
            double y1p = -sinPhi * dx + cosPhi * dy;

            double rxSq = rx * rx;
            double rySq = ry * ry;
            double x1pSq = x1p * x1p;
            double y1pSq = y1p * y1p;

            // Radii check & scale up if necessary
            double radiiCheck = x1pSq / rxSq + y1pSq / rySq;
            if (radiiCheck > 1.0)
            {
                double scale = Math.Sqrt(radiiCheck);
                rx = (float)(rx * scale);
                ry = (float)(ry * scale);
                rxSq = rx * rx;
                rySq = ry * ry;
            }

            double sign = (largeArc == sweep) ? -1.0 : 1.0;
            double num = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
            double den = rxSq * y1pSq + rySq * x1pSq;
            double rad = Math.Max(0.0, num / Math.Max(1e-9, den));
            double factor = sign * Math.Sqrt(rad);

            double cxp = factor * (rx * y1p / ry);
            double cyp = factor * (-ry * x1p / rx);

            double cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0;
            double cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0;

            double v1x = (x1p - cxp) / rx;
            double v1y = (y1p - cyp) / ry;
            double v2x = (-x1p - cxp) / rx;
            double v2y = (-y1p - cyp) / ry;

            double theta1 = VectorAngle(1.0, 0.0, v1x, v1y);
            double dTheta = VectorAngle(v1x, v1y, v2x, v2y);

            if (!sweep && dTheta > 0) dTheta -= 2.0 * Math.PI;
            else if (sweep && dTheta < 0) dTheta += 2.0 * Math.PI;

            int segments = (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI / 2.0));
            double delta = dTheta / segments;
            double t = 8.0 / 3.0 * Math.Sin(delta / 4.0) * Math.Sin(delta / 4.0) / Math.Sin(delta / 2.0);

            double curTheta = theta1;
            for (int i = 0; i < segments; i++)
            {
                double nextTheta = curTheta + delta;

                double cosTheta = Math.Cos(curTheta);
                double sinTheta = Math.Sin(curTheta);
                double cosNextTheta = Math.Cos(nextTheta);
                double sinNextTheta = Math.Sin(nextTheta);

                double ep1x = cosPhi * rx * cosTheta - sinPhi * ry * sinTheta + cx;
                double ep1y = sinPhi * rx * cosTheta + cosPhi * ry * sinTheta + cy;

                double d1x = -cosPhi * rx * sinTheta - sinPhi * ry * cosTheta;
                double d1y = -sinPhi * rx * sinTheta + cosPhi * ry * cosTheta;

                double ep2x = cosPhi * rx * cosNextTheta - sinPhi * ry * sinNextTheta + cx;
                double ep2y = sinPhi * rx * cosNextTheta + cosPhi * ry * sinNextTheta + cy;

                double d2x = -cosPhi * rx * sinNextTheta - sinPhi * ry * cosNextTheta;
                double d2y = -sinPhi * rx * sinNextTheta + cosPhi * ry * cosNextTheta;

                float cp1x = (float)(ep1x + t * d1x);
                float cp1y = (float)(ep1y + t * d1y);
                float cp2x = (float)(ep2x - t * d2x);
                float cp2y = (float)(ep2y - t * d2y);
                float endPtX = (float)ep2x;
                float endPtY = (float)ep2y;

                if (i == segments - 1)
                {
                    endPtX = x2;
                    endPtY = y2;
                }

                path.AddBezier((float)ep1x, (float)ep1y, cp1x, cp1y, cp2x, cp2y, endPtX, endPtY);
                curTheta = nextTheta;
            }
        }

        private static double VectorAngle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
            double sign = (ux * vy - uy * vx < 0) ? -1.0 : 1.0;
            double cos = Math.Max(-1.0, Math.Min(1.0, dot / Math.Max(1e-9, len)));
            return sign * Math.Acos(cos);
        }

        private enum TokenType { Command, Number }

        private struct PathToken
        {
            public TokenType Type;
            public char Command;
            public float Number;
        }

        private static List<PathToken> Tokenize(string s)
        {
            List<PathToken> list = new List<PathToken>();
            if (string.IsNullOrEmpty(s)) return list;

            int i = 0;
            int n = s.Length;

            while (i < n)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }

                if (char.IsLetter(c))
                {
                    list.Add(new PathToken { Type = TokenType.Command, Command = c });
                    i++;
                    continue;
                }

                if (c == '-' || c == '+' || c == '.' || char.IsDigit(c))
                {
                    int start = i;
                    bool hasDot = (c == '.');
                    i++;

                    while (i < n)
                    {
                        char ch = s[i];
                        if (char.IsDigit(ch)) { i++; }
                        else if (ch == '.' && !hasDot) { hasDot = true; i++; }
                        else if ((ch == 'e' || ch == 'E') && i + 1 < n)
                        {
                            i++;
                            if (s[i] == '+' || s[i] == '-') i++;
                        }
                        else break;
                    }

                    string sub = s.Substring(start, i - start);
                    float val;
                    if (float.TryParse(sub, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    {
                        list.Add(new PathToken { Type = TokenType.Number, Number = val });
                    }
                    continue;
                }
                i++;
            }
            return list;
        }
    }
}

